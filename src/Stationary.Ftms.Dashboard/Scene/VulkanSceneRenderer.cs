using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

using Stationary.Ftms.Dashboard.Core;

using Device = Silk.NET.Vulkan.Device;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImage = Silk.NET.Vulkan.Image;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Stationary.Ftms.Dashboard.Scene;

[StructLayout(LayoutKind.Sequential)]
internal struct ScenePushConstants
{
    public Matrix4x4 ViewProjection;
    public Matrix4x4 Model;
    public Vector4 Color;
    public Vector4 Camera;
    public Vector4 Fog;
    public Vector4 Effects;
    public Vector4 Heart;
    public Vector4 Tint;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PostPushConstants
{
    public Vector4 Settings;
    public Vector4 Extra;
}

// Renders the ride scene with Vulkan on a dedicated thread. On Apple platforms Vulkan runs through MoltenVK (statically
// linked from Silk.NET.MoltenVK.Native) onto a CAMetalLayer. The UI thread only publishes the latest frame and size.
// Each frame renders the scene in HDR (mirrored reflections, the wet floor blended over them, opaque geometry, then
// additive glow), blurs its highlights through a bloom mip chain, and composites with filmic tone mapping.
internal sealed unsafe partial class VulkanSceneRenderer : IDisposable
{
    private const Format DepthFormat = Format.D32Sfloat;
    private const Format HdrFormat = Format.R16G16B16A16Sfloat;
    private const int BloomLevels = 5;
    private const string MoltenVkArgumentBuffers = "MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS";
    private const float BloomThreshold = 1.4f;
    private const float BloomStrength = 0.22f;
    private const float Exposure = 0.9f;
    private const float ChromaticAberration = 0.004f;
    private static readonly string[] RequiredInstanceExtensions = [KhrSurface.ExtensionName, ExtMetalSurface.ExtensionName];
    private static readonly Matrix4x4 Mirror = Matrix4x4.CreateScale(1f, -1f, 1f);
    private const string PortabilityEnumerationExtension = "VK_KHR_portability_enumeration";
    private const string PortabilitySubsetExtension = "VK_KHR_portability_subset";

    private readonly nint metalLayer;
    private readonly Thread thread;
    private readonly RideSceneComposer composer = new();
    private readonly List<SceneDraw> draws = [];
    private readonly RenderTarget[] bloom = new RenderTarget[BloomLevels];
    private readonly DescriptorSet[] downSets = new DescriptorSet[BloomLevels];
    private readonly DescriptorSet[] upSets = new DescriptorSet[BloomLevels];
    private volatile RideSceneFrame latestFrame = RideSceneFrame.Idle;
    private volatile bool stopping;
    private long requestedSize;

    private Vk vk = null!;
    private Instance instance;
    private KhrSurface khrSurface = null!;
    private SurfaceKHR surface;
    private PhysicalDevice physicalDevice;
    private Device device;
    private Queue queue;
    private uint queueFamily;
    private KhrSwapchain khrSwapchain = null!;
    private SwapchainKHR swapchain;
    private Format swapchainFormat;
    private Extent2D extent;
    private VkImage[] swapchainImages = [];
    private ImageView[] swapchainViews = [];
    private Framebuffer[] framebuffers = [];
    private VkSemaphore[] renderFinished = [];
    private RenderTarget depth;
    private RenderTarget hdr;
    private DescriptorSet compositeSet;
    private RenderPass scenePass;
    private RenderPass downPass;
    private RenderPass upPass;
    private RenderPass compositePass;
    private PipelineLayout pipelineLayout;
    private Pipeline opaquePipeline;
    private Pipeline glowPipeline;
    private Pipeline surfacePipeline;
    private DescriptorSetLayout postSetLayout;
    private PipelineLayout postLayout;
    private Pipeline downPipeline;
    private Pipeline upPipeline;
    private Pipeline compositePipeline;
    private Sampler sampler;
    private DescriptorPool descriptorPool;
    private VkBuffer vertexBuffer;
    private DeviceMemory vertexMemory;
    private VkBuffer indexBuffer;
    private DeviceMemory indexMemory;
    private SceneMeshRange[] meshes = [];
    private CommandPool commandPool;
    private CommandBuffer commandBuffer;
    private VkSemaphore imageAvailable;
    private Fence inFlight;
#if DEBUG
    private readonly SceneCapture capturer = new();
#endif

    public VulkanSceneRenderer(nint metalLayer)
    {
        this.metalLayer = metalLayer;
        thread = new(Run) { IsBackground = true, Name = "Ride scene renderer" };
    }

    private enum BlendMode
    {
        Opaque,
        Additive,
        Premultiplied,
    }

    public void Start() => thread.Start();

    public void Update(RideSceneFrame frame)
    {
#if DEBUG
        if (SceneDemo.IsEnabled)
        {
            return;
        }
#endif
        latestFrame = frame;
    }

    public void Resize(uint width, uint height) => Interlocked.Exchange(ref requestedSize, ((long)width << 32) | height);

    public void Dispose()
    {
        stopping = true;
        if (thread.IsAlive)
        {
            thread.Join(TimeSpan.FromSeconds(3));
        }
    }

    [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetNativeEnvironmentVariable(string name, string value, int overwrite);

    private static (uint Width, uint Height) Unpack(long size) => ((uint)(size >> 32), (uint)(size & 0xFFFF_FFFF));

    private void Run()
    {
        try
        {
            Initialize();
            var clock = Stopwatch.StartNew();
            var last = clock.Elapsed;
            long builtSize = 0;
            while (!stopping)
            {
                var size = Interlocked.Read(ref requestedSize);
                var (width, height) = Unpack(size);
                if (width == 0 || height == 0)
                {
                    Thread.Sleep(50);
                    continue;
                }

                if (size != builtSize)
                {
                    RecreateSwapchain(width, height);
                    builtSize = size;
                }

                var now = clock.Elapsed;
                var seconds = (float)double.Min((now - last).TotalSeconds, 0.1d);
                last = now;
#if DEBUG
                if (SceneDemo.IsEnabled)
                {
                    latestFrame = SceneDemo.CreateFrame(now.TotalSeconds);
                }
#endif
                composer.Advance(latestFrame, seconds);
                if (!DrawFrame(now))
                {
                    builtSize = 0;
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Trace.TraceError("The 3D ride scene stopped: {0}", exception);
        }
        finally
        {
            Cleanup();
        }
    }

    private void Initialize()
    {
        AppContext.SetSwitch("SILK_NET_VULKAN_VK_ENABLE_PINVOKE_OVERRIDE_0", true);
        // MoltenVK skips Metal hazard tracking for textures bound through argument buffers, so the small bloom
        // passes read their source level before the previous pass has written it. Classic binding is tracked.
        // MoltenVK reads this setting with getenv when the instance is created; .NET does not update the native environment.
        _ = SetNativeEnvironmentVariable(MoltenVkArgumentBuffers, "0", overwrite: 0);

        vk = Vk.GetApi();
        CreateInstance();
        if (!vk.TryGetInstanceExtension(instance, out ExtMetalSurface metalSurface) || !vk.TryGetInstanceExtension(instance, out khrSurface))
        {
            throw new InvalidOperationException("Vulkan Metal surface extensions are unavailable.");
        }

        var surfaceInfo = new MetalSurfaceCreateInfoEXT { SType = StructureType.MetalSurfaceCreateInfoExt, PLayer = (nint*)metalLayer };
        Check(metalSurface.CreateMetalSurface(instance, in surfaceInfo, null, out surface), "create the Metal surface");
        PickDevice();
        CreateDevice();
        scenePass = CreateRenderPass(HdrFormat, AttachmentLoadOp.Clear, ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal, withDepth: true);
        downPass = CreateRenderPass(HdrFormat, AttachmentLoadOp.DontCare, ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal, withDepth: false);
        upPass = CreateRenderPass(HdrFormat, AttachmentLoadOp.Load, ImageLayout.ShaderReadOnlyOptimal, ImageLayout.ShaderReadOnlyOptimal, withDepth: false);
        compositePass = CreateRenderPass(swapchainFormat, AttachmentLoadOp.DontCare, ImageLayout.Undefined, ImageLayout.PresentSrcKhr, withDepth: false);
        CreateScenePipelines();
        CreatePostPipelines();
        CreateMeshBuffers();

        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = queueFamily,
        };
        Check(vk.CreateCommandPool(device, in poolInfo, null, out commandPool), "create the command pool");
        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Check(vk.AllocateCommandBuffers(device, in allocateInfo, out commandBuffer), "allocate the command buffer");
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        Check(vk.CreateSemaphore(device, in semaphoreInfo, null, out imageAvailable), "create a semaphore");
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
        Check(vk.CreateFence(device, in fenceInfo, null, out inFlight), "create a fence");
    }

    private void CreateInstance()
    {
        uint count = 0;
        vk.EnumerateInstanceExtensionProperties((byte*)null, ref count, null);
        var available = new ExtensionProperties[count];
        fixed (ExtensionProperties* properties = available)
        {
            vk.EnumerateInstanceExtensionProperties((byte*)null, ref count, properties);
        }

        var names = available.Select(static property => SilkMarshal.PtrToString((nint)property.ExtensionName)).ToHashSet();
        var extensions = RequiredInstanceExtensions.ToList();
        var flags = InstanceCreateFlags.None;
        if (names.Contains(PortabilityEnumerationExtension))
        {
            extensions.Add(PortabilityEnumerationExtension);
            flags |= InstanceCreateFlags.EnumeratePortabilityBitKhr;
        }

        var applicationName = (byte*)SilkMarshal.StringToPtr("Stationary");
        var extensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions);
        try
        {
            var applicationInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = applicationName,
                ApplicationVersion = Vk.MakeVersion(1, 0),
                PEngineName = applicationName,
                EngineVersion = Vk.MakeVersion(1, 0),
                ApiVersion = Vk.Version12,
            };
            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &applicationInfo,
                Flags = flags,
                EnabledExtensionCount = (uint)extensions.Count,
                PpEnabledExtensionNames = extensionNames,
            };
            Check(vk.CreateInstance(in createInfo, null, out instance), "create the Vulkan instance");
        }
        finally
        {
            SilkMarshal.Free((nint)applicationName);
            SilkMarshal.FreeString((nint)extensionNames);
        }
    }

    private void PickDevice()
    {
        foreach (var candidate in vk.GetPhysicalDevices(instance))
        {
            vk.GetPhysicalDeviceProperties(candidate, out var deviceProperties);
            if (deviceProperties.Limits.MaxPushConstantsSize < (uint)sizeof(ScenePushConstants))
            {
                continue;
            }

            uint familyCount = 0;
            vk.GetPhysicalDeviceQueueFamilyProperties(candidate, ref familyCount, null);
            var families = new QueueFamilyProperties[familyCount];
            fixed (QueueFamilyProperties* properties = families)
            {
                vk.GetPhysicalDeviceQueueFamilyProperties(candidate, ref familyCount, properties);
            }

            for (uint index = 0; index < familyCount; index++)
            {
                khrSurface.GetPhysicalDeviceSurfaceSupport(candidate, index, surface, out var presentSupported);
                if ((families[index].QueueFlags & QueueFlags.GraphicsBit) != 0 && presentSupported)
                {
                    physicalDevice = candidate;
                    queueFamily = index;
                    return;
                }
            }
        }

        throw new InvalidOperationException("No Vulkan device can present to the scene window.");
    }

    private void CreateDevice()
    {
        uint count = 0;
        vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, ref count, null);
        var available = new ExtensionProperties[count];
        fixed (ExtensionProperties* properties = available)
        {
            vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, ref count, properties);
        }

        var extensions = new List<string> { KhrSwapchain.ExtensionName };
        if (available.Any(static property => SilkMarshal.PtrToString((nint)property.ExtensionName) == PortabilitySubsetExtension))
        {
            extensions.Add(PortabilitySubsetExtension);
        }

        var priority = 1f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = queueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };
        var extensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions);
        try
        {
            var features = new PhysicalDeviceFeatures();
            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueInfo,
                PEnabledFeatures = &features,
                EnabledExtensionCount = (uint)extensions.Count,
                PpEnabledExtensionNames = extensionNames,
            };
            Check(vk.CreateDevice(physicalDevice, in createInfo, null, out device), "create the Vulkan device");
        }
        finally
        {
            SilkMarshal.FreeString((nint)extensionNames);
        }

        vk.GetDeviceQueue(device, queueFamily, 0, out queue);
        if (!vk.TryGetDeviceExtension(instance, device, out khrSwapchain))
        {
            throw new InvalidOperationException("The Vulkan swapchain extension is unavailable.");
        }

        swapchainFormat = ChooseSurfaceFormat();
    }

    private Format ChooseSurfaceFormat()
    {
        uint count = 0;
        khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, surface, ref count, null);
        var formats = new SurfaceFormatKHR[count];
        fixed (SurfaceFormatKHR* pointer = formats)
        {
            khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, surface, ref count, pointer);
        }

        return formats.Any(static format => format.Format == Format.B8G8R8A8Srgb) ? Format.B8G8R8A8Srgb : formats[0].Format;
    }

    // Every pass hands its colour target to the next pass as a sampled texture, so the dependencies cover both
    // attachment writes before shader reads and shader reads before the next overwrite.
    private RenderPass CreateRenderPass(Format format, AttachmentLoadOp load, ImageLayout initial, ImageLayout final, bool withDepth)
    {
        var attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new()
        {
            Format = format,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = initial,
            FinalLayout = final,
        };
        attachments[1] = new()
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };
        var colorReference = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        var depthReference = new AttachmentReference { Attachment = 1, Layout = ImageLayout.DepthStencilAttachmentOptimal };
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
            PDepthStencilAttachment = withDepth ? &depthReference : null,
        };
        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new()
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit
                | AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit,
        };
        dependencies[1] = new()
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.TransferBit,
            DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.TransferReadBit,
        };
        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = withDepth ? 2u : 1u,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };
        Check(vk.CreateRenderPass(device, in renderPassInfo, null, out var created), "create a render pass");
        return created;
    }

    private void CreateScenePipelines()
    {
        var pushRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = (uint)sizeof(ScenePushConstants),
        };
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        Check(vk.CreatePipelineLayout(device, in layoutInfo, null, out pipelineLayout), "create the pipeline layout");

        var vertexModule = CreateShaderModule("scene.vert.spv");
        var fragmentModule = CreateShaderModule("scene.frag.spv");
        var entryPoint = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = vertexModule, PName = entryPoint };
            stages[1] = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = fragmentModule, PName = entryPoint };
            opaquePipeline = CreatePipeline(stages, scenePass, pipelineLayout, BlendMode.Opaque, scene: true);
            glowPipeline = CreatePipeline(stages, scenePass, pipelineLayout, BlendMode.Additive, scene: true);
            surfacePipeline = CreatePipeline(stages, scenePass, pipelineLayout, BlendMode.Premultiplied, scene: true);
        }
        finally
        {
            SilkMarshal.Free((nint)entryPoint);
            vk.DestroyShaderModule(device, vertexModule, null);
            vk.DestroyShaderModule(device, fragmentModule, null);
        }
    }

    private void CreatePostPipelines()
    {
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MaxLod = 0f,
        };
        Check(vk.CreateSampler(device, in samplerInfo, null, out sampler), "create the sampler");

        var bindings = stackalloc DescriptorSetLayoutBinding[2];
        for (uint binding = 0; binding < 2; binding++)
        {
            bindings[binding] = new()
            {
                Binding = binding,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            };
        }

        var setLayoutInfo = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 2, PBindings = bindings };
        Check(vk.CreateDescriptorSetLayout(device, in setLayoutInfo, null, out postSetLayout), "create the descriptor set layout");
        var pushRange = new PushConstantRange { StageFlags = ShaderStageFlags.FragmentBit, Offset = 0, Size = (uint)sizeof(PostPushConstants) };
        var setLayout = postSetLayout;
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushRange,
        };
        Check(vk.CreatePipelineLayout(device, in layoutInfo, null, out postLayout), "create the post-processing layout");

        // One set per bloom read: each down and up step, plus the composite.
        var poolSize = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = BloomLevels * 4 + 2 };
        var poolInfo = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = BloomLevels * 2 + 1, PoolSizeCount = 1, PPoolSizes = &poolSize };
        Check(vk.CreateDescriptorPool(device, in poolInfo, null, out descriptorPool), "create the descriptor pool");

        var vertexModule = CreateShaderModule("post.vert.spv");
        var downModule = CreateShaderModule("bloom_down.frag.spv");
        var upModule = CreateShaderModule("bloom_up.frag.spv");
        var compositeModule = CreateShaderModule("composite.frag.spv");
        var entryPoint = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = vertexModule, PName = entryPoint };
            stages[1] = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = downModule, PName = entryPoint };
            downPipeline = CreatePipeline(stages, downPass, postLayout, BlendMode.Opaque, scene: false);
            stages[1].Module = upModule;
            upPipeline = CreatePipeline(stages, upPass, postLayout, BlendMode.Additive, scene: false);
            stages[1].Module = compositeModule;
            compositePipeline = CreatePipeline(stages, compositePass, postLayout, BlendMode.Opaque, scene: false);
        }
        finally
        {
            SilkMarshal.Free((nint)entryPoint);
            vk.DestroyShaderModule(device, vertexModule, null);
            vk.DestroyShaderModule(device, downModule, null);
            vk.DestroyShaderModule(device, upModule, null);
            vk.DestroyShaderModule(device, compositeModule, null);
        }
    }

    private Pipeline CreatePipeline(PipelineShaderStageCreateInfo* stages, RenderPass pass, PipelineLayout layout, BlendMode blend, bool scene)
    {
        var binding = new VertexInputBindingDescription { Binding = 0, Stride = (uint)sizeof(SceneVertex), InputRate = VertexInputRate.Vertex };
        var attributes = stackalloc VertexInputAttributeDescription[2];
        attributes[0] = new() { Location = 0, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 0 };
        attributes[1] = new() { Location = 1, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 12 };
        var vertexInput = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = scene ? 1u : 0u,
            PVertexBindingDescriptions = &binding,
            VertexAttributeDescriptionCount = scene ? 2u : 0u,
            PVertexAttributeDescriptions = attributes,
        };
        var inputAssembly = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList,
        };
        var viewportState = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1,
        };
        var rasterizer = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullModeFlags.None,
            FrontFace = FrontFace.CounterClockwise,
            LineWidth = 1f,
        };
        var multisample = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit,
        };
        var depthStencil = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = blend != BlendMode.Additive,
            DepthCompareOp = CompareOp.LessOrEqual,
        };
        // Additive light layers over the scene without occluding it; premultiplied surfaces let reflections show through.
        var blendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = blend != BlendMode.Opaque,
            SrcColorBlendFactor = BlendFactor.One,
            DstColorBlendFactor = blend == BlendMode.Premultiplied ? BlendFactor.OneMinusSrcAlpha : BlendFactor.One,
            ColorBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = BlendFactor.Zero,
            DstAlphaBlendFactor = BlendFactor.One,
            AlphaBlendOp = BlendOp.Add,
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
        };
        var colorBlend = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = 1,
            PAttachments = &blendAttachment,
        };
        var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
        var dynamicState = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates,
        };
        var pipelineInfo = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            StageCount = 2,
            PStages = stages,
            PVertexInputState = &vertexInput,
            PInputAssemblyState = &inputAssembly,
            PViewportState = &viewportState,
            PRasterizationState = &rasterizer,
            PMultisampleState = &multisample,
            PDepthStencilState = scene ? &depthStencil : null,
            PColorBlendState = &colorBlend,
            PDynamicState = &dynamicState,
            Layout = layout,
            RenderPass = pass,
            Subpass = 0,
        };
        Check(vk.CreateGraphicsPipelines(device, default, 1, in pipelineInfo, null, out var created), "create a graphics pipeline");
        return created;
    }

    private ShaderModule CreateShaderModule(string resourceName)
    {
        using var stream = typeof(VulkanSceneRenderer).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The shader '{resourceName}' is not embedded.");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        fixed (byte* pointer = code)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)pointer,
            };
            Check(vk.CreateShaderModule(device, in info, null, out var module), $"create the shader '{resourceName}'");
            return module;
        }
    }

    private void CreateMeshBuffers()
    {
        var (vertices, indices, ranges) = SceneMeshes.Build();
        meshes = ranges;
        (vertexBuffer, vertexMemory) = CreateHostBuffer<SceneVertex>(vertices, BufferUsageFlags.VertexBufferBit);
        (indexBuffer, indexMemory) = CreateHostBuffer<ushort>(indices, BufferUsageFlags.IndexBufferBit);
    }

    private (VkBuffer Buffer, DeviceMemory Memory) CreateHostBuffer<T>(ReadOnlySpan<T> data, BufferUsageFlags usage)
        where T : unmanaged
    {
        var size = (ulong)(data.Length * sizeof(T));
        var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = usage, SharingMode = SharingMode.Exclusive };
        Check(vk.CreateBuffer(device, in info, null, out var buffer), "create a buffer");
        vk.GetBufferMemoryRequirements(device, buffer, out var requirements);
        var memory = Allocate(requirements, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(vk.BindBufferMemory(device, buffer, memory, 0), "bind buffer memory");
        void* mapped;
        Check(vk.MapMemory(device, memory, 0, size, 0, &mapped), "map buffer memory");
        data.CopyTo(new Span<T>(mapped, data.Length));
        vk.UnmapMemory(device, memory);
        return (buffer, memory);
    }

    private DeviceMemory Allocate(MemoryRequirements requirements, MemoryPropertyFlags properties)
    {
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out var memoryProperties);
        for (var index = 0; index < memoryProperties.MemoryTypeCount; index++)
        {
            if ((requirements.MemoryTypeBits & (1u << index)) != 0 && (memoryProperties.MemoryTypes[index].PropertyFlags & properties) == properties)
            {
                var info = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = requirements.Size, MemoryTypeIndex = (uint)index };
                Check(vk.AllocateMemory(device, in info, null, out var memory), "allocate device memory");
                return memory;
            }
        }

        throw new InvalidOperationException("No suitable Vulkan memory type.");
    }

    private void RecreateSwapchain(uint width, uint height)
    {
        vk.DeviceWaitIdle(device);
        DestroySwapchainResources(keepSwapchain: true);
        khrSurface.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, surface, out var capabilities);
        extent = capabilities.CurrentExtent.Width is not (uint.MaxValue or 0)
            ? capabilities.CurrentExtent
            : new Extent2D(
                uint.Clamp(width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
                uint.Clamp(height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height));
        var imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0)
        {
            imageCount = uint.Min(imageCount, capabilities.MaxImageCount);
        }

        var usage = ImageUsageFlags.ColorAttachmentBit;
#if DEBUG
        if (SceneDemo.CaptureDirectory is not null && (capabilities.SupportedUsageFlags & ImageUsageFlags.TransferSrcBit) != 0)
        {
            usage |= ImageUsageFlags.TransferSrcBit;
        }

        capturer.IsSupported = (usage & ImageUsageFlags.TransferSrcBit) != 0;
#endif
        var oldSwapchain = swapchain;
        var info = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = surface,
            MinImageCount = imageCount,
            ImageFormat = swapchainFormat,
            ImageColorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = usage,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = PresentModeKHR.FifoKhr,
            Clipped = true,
            OldSwapchain = oldSwapchain,
        };
        Check(khrSwapchain.CreateSwapchain(device, in info, null, out swapchain), "create the swapchain");
        if (oldSwapchain.Handle != 0)
        {
            khrSwapchain.DestroySwapchain(device, oldSwapchain, null);
        }

        uint count = 0;
        khrSwapchain.GetSwapchainImages(device, swapchain, ref count, null);
        swapchainImages = new VkImage[count];
        fixed (VkImage* images = swapchainImages)
        {
            khrSwapchain.GetSwapchainImages(device, swapchain, ref count, images);
        }

        CreateRenderTargets();
        swapchainViews = new ImageView[count];
        framebuffers = new Framebuffer[count];
        renderFinished = new VkSemaphore[count];
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        for (var index = 0; index < count; index++)
        {
            swapchainViews[index] = CreateView(swapchainImages[index], swapchainFormat, ImageAspectFlags.ColorBit);
            framebuffers[index] = CreateFramebuffer(compositePass, extent, swapchainViews[index]);
            Check(vk.CreateSemaphore(device, in semaphoreInfo, null, out renderFinished[index]), "create a semaphore");
        }
    }

    // The HDR scene target and depth at full size, plus bloom levels at half, quarter, ... resolution.
    private void CreateRenderTargets()
    {
        depth = CreateTarget(extent, DepthFormat, ImageUsageFlags.DepthStencilAttachmentBit, ImageAspectFlags.DepthBit);
        hdr = CreateTarget(extent, HdrFormat, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit, ImageAspectFlags.ColorBit);
        hdr = hdr with { Framebuffer = CreateFramebuffer(scenePass, extent, hdr.View, depth.View) };
        for (var level = 0; level < BloomLevels; level++)
        {
            var size = new Extent2D(uint.Max(1u, extent.Width >> (level + 1)), uint.Max(1u, extent.Height >> (level + 1)));
            var target = CreateTarget(size, HdrFormat, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit, ImageAspectFlags.ColorBit);
            bloom[level] = target with { Framebuffer = CreateFramebuffer(downPass, size, target.View) };
        }

        Check(vk.ResetDescriptorPool(device, descriptorPool, 0), "reset the descriptor pool");
        for (var level = 0; level < BloomLevels; level++)
        {
            downSets[level] = AllocateSet(level == 0 ? hdr.View : bloom[level - 1].View, default);
            upSets[level] = AllocateSet(bloom[level].View, default);
        }

        compositeSet = AllocateSet(hdr.View, bloom[0].View);
    }

    private RenderTarget CreateTarget(Extent2D size, Format format, ImageUsageFlags usage, ImageAspectFlags aspect)
    {
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D(size.Width, size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(vk.CreateImage(device, in info, null, out var image), "create a render target");
        vk.GetImageMemoryRequirements(device, image, out var requirements);
        var memory = Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
        Check(vk.BindImageMemory(device, image, memory, 0), "bind render target memory");
        return new(image, memory, CreateView(image, format, aspect), default, size);
    }

    private Framebuffer CreateFramebuffer(RenderPass pass, Extent2D size, ImageView color, ImageView depthView = default)
    {
        var views = stackalloc ImageView[2] { color, depthView };
        var info = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = pass,
            AttachmentCount = depthView.Handle != 0 ? 2u : 1u,
            PAttachments = views,
            Width = size.Width,
            Height = size.Height,
            Layers = 1,
        };
        Check(vk.CreateFramebuffer(device, in info, null, out var framebuffer), "create a framebuffer");
        return framebuffer;
    }

    private DescriptorSet AllocateSet(ImageView first, ImageView second)
    {
        var layout = postSetLayout;
        var allocateInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };
        Check(vk.AllocateDescriptorSets(device, in allocateInfo, out var set), "allocate a descriptor set");
        var images = stackalloc DescriptorImageInfo[2];
        images[0] = new(sampler, first, ImageLayout.ShaderReadOnlyOptimal);
        images[1] = new(sampler, second.Handle != 0 ? second : first, ImageLayout.ShaderReadOnlyOptimal);
        var writes = stackalloc WriteDescriptorSet[2];
        for (uint binding = 0; binding < 2; binding++)
        {
            writes[binding] = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = binding,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = &images[binding],
            };
        }

        vk.UpdateDescriptorSets(device, 2, writes, 0, null);
        return set;
    }

    private ImageView CreateView(VkImage image, Format format, ImageAspectFlags aspect)
    {
        var info = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange(aspect, 0, 1, 0, 1),
        };
        Check(vk.CreateImageView(device, in info, null, out var view), "create an image view");
        return view;
    }

    private bool DrawFrame(TimeSpan now)
    {
        vk.WaitForFences(device, 1, in inFlight, true, ulong.MaxValue);
        uint imageIndex = 0;
        var acquired = khrSwapchain.AcquireNextImage(device, swapchain, 1_000_000_000UL, imageAvailable, default, ref imageIndex);
        if (acquired == Result.ErrorOutOfDateKhr)
        {
            return false;
        }

        if (acquired is not (Result.Success or Result.SuboptimalKhr))
        {
            Thread.Sleep(16);
            return true;
        }

        vk.ResetFences(device, 1, in inFlight);
        var capture = false;
#if DEBUG
        capture = capturer.ShouldCapture(now);
#endif
        Record(imageIndex, (float)now.TotalSeconds, capture);
        var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
        var available = imageAvailable;
        var finished = renderFinished[imageIndex];
        var commands = commandBuffer;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &available,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &commands,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &finished,
        };
        Check(vk.QueueSubmit(queue, 1, in submit, inFlight), "submit the frame");
#if DEBUG
        if (capture)
        {
            vk.WaitForFences(device, 1, in inFlight, true, ulong.MaxValue);
            capturer.Save(vk, device, extent);
        }
#endif
        var chain = swapchain;
        var present = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &finished,
            SwapchainCount = 1,
            PSwapchains = &chain,
            PImageIndices = &imageIndex,
        };
        var presented = khrSwapchain.QueuePresent(queue, in present);
        return presented is not (Result.ErrorOutOfDateKhr or Result.SuboptimalKhr) && acquired != Result.SuboptimalKhr;
    }

    private void Record(uint imageIndex, float seconds, bool capture)
    {
        vk.ResetCommandBuffer(commandBuffer, 0);
        var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        Check(vk.BeginCommandBuffer(commandBuffer, in beginInfo), "begin the command buffer");
        RecordScene();
        RecordBloom();

        BeginPass(compositePass, framebuffers[imageIndex], extent, clearDepth: false);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, compositePipeline);
        var settings = new PostPushConstants
        {
            Settings = new(BloomStrength, Exposure, ChromaticAberration, seconds % 100f),
            Extra = new(swapchainFormat == Format.B8G8R8A8Srgb ? 0f : 1f, 0f, 0f, 0f),
        };
        DrawFullscreen(compositeSet, settings);
        vk.CmdEndRenderPass(commandBuffer);
#if DEBUG
        if (capture)
        {
            capturer.Record(vk, device, physicalDevice, commandBuffer, swapchainImages[imageIndex], extent);
        }
#else
        _ = capture;
#endif
        Check(vk.EndCommandBuffer(commandBuffer), "end the command buffer");
    }

    private void RecordScene()
    {
        BeginPass(scenePass, hdr.Framebuffer, extent, clearDepth: true);
        ulong offset = 0;
        var vertices = vertexBuffer;
        vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertices, &offset);
        vk.CmdBindIndexBuffer(commandBuffer, indexBuffer, 0, IndexType.Uint16);

        var camera = composer.Compose(latestFrame, extent.Width / (float)extent.Height, draws);
        var push = new ScenePushConstants
        {
            ViewProjection = camera.ViewProjection,
            Camera = new Vector4(camera.Position, camera.FogDensity),
            Fog = new Vector4(SrgbToLinear(camera.FogColor), camera.Time),
            Tint = new Vector4(SrgbToLinear(camera.ZoneColor), 0f),
        };

        // The mirrored world goes first, below the floor; the wet floor blends over it, then the real world on top.
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, opaquePipeline);
        DrawPass(ref push, camera, static material => !material.IsAdditive() && !material.IsReflective(), mirrored: true);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, glowPipeline);
        DrawPass(ref push, camera, static material => material.IsAdditive(), mirrored: true);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, surfacePipeline);
        DrawPass(ref push, camera, static material => material == SceneMaterial.Track, mirrored: false);
        DrawPass(ref push, camera, static material => material == SceneMaterial.Grid, mirrored: false);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, opaquePipeline);
        DrawPass(ref push, camera, static material => !material.IsAdditive() && !material.IsReflective(), mirrored: false);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, glowPipeline);
        DrawPass(ref push, camera, static material => material.IsAdditive(), mirrored: false);
        vk.CmdEndRenderPass(commandBuffer);
    }

    // Downsample the bright parts of the HDR image through the chain, then upsample back, adding each blurrier level.
    private void RecordBloom()
    {
        for (var level = 0; level < BloomLevels; level++)
        {
            var source = level == 0 ? extent : bloom[level - 1].Extent;
            BeginPass(downPass, bloom[level].Framebuffer, bloom[level].Extent, clearDepth: false);
            vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, downPipeline);
            DrawFullscreen(downSets[level], new() { Settings = new(1f / source.Width, 1f / source.Height, BloomThreshold, level == 0 ? 1f : 0f) });
            vk.CmdEndRenderPass(commandBuffer);
        }

        for (var level = BloomLevels - 1; level > 0; level--)
        {
            var source = bloom[level].Extent;
            BeginPass(upPass, bloom[level - 1].Framebuffer, bloom[level - 1].Extent, clearDepth: false);
            vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, upPipeline);
            DrawFullscreen(upSets[level], new() { Settings = new(1f / source.Width, 1f / source.Height, 1f, 1f) });
            vk.CmdEndRenderPass(commandBuffer);
        }
    }

    private void BeginPass(RenderPass pass, Framebuffer framebuffer, Extent2D size, bool clearDepth)
    {
        var clears = stackalloc ClearValue[2];
        clears[0] = new(new ClearColorValue(0f, 0f, 0f, 1f));
        clears[1] = new(depthStencil: new ClearDepthStencilValue(1f, 0));
        var renderPassBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = pass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), size),
            ClearValueCount = clearDepth ? 2u : 1u,
            PClearValues = clears,
        };
        vk.CmdBeginRenderPass(commandBuffer, in renderPassBegin, SubpassContents.Inline);
        var viewport = new Viewport(0f, 0f, size.Width, size.Height, 0f, 1f);
        vk.CmdSetViewport(commandBuffer, 0, 1, in viewport);
        var scissor = new Rect2D(new Offset2D(0, 0), size);
        vk.CmdSetScissor(commandBuffer, 0, 1, in scissor);
    }

    private void DrawFullscreen(DescriptorSet set, PostPushConstants settings)
    {
        vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, postLayout, 0, 1, in set, 0, null);
        vk.CmdPushConstants(commandBuffer, postLayout, ShaderStageFlags.FragmentBit, 0, (uint)sizeof(PostPushConstants), &settings);
        vk.CmdDraw(commandBuffer, 3, 1, 0, 0);
    }

    private void DrawPass(ref ScenePushConstants push, SceneCamera camera, Func<SceneMaterial, bool> include, bool mirrored)
    {
        push.Heart = new Vector4(camera.HeartPhase, camera.HeartRate, camera.HeartPulse, mirrored ? 1f : 0f);
        foreach (var draw in draws)
        {
            if (!include(draw.Material))
            {
                continue;
            }

            var mesh = meshes[(int)draw.Mesh];
            push.Model = mirrored ? draw.Model * Mirror : draw.Model;
            push.Color = new Vector4(SrgbToLinear(new Vector3(draw.Color.X, draw.Color.Y, draw.Color.Z)), draw.Color.W);
            push.Effects = new Vector4((float)draw.Material, camera.Scroll, camera.Beat, camera.Energy);
            push.Tint.W = draw.Param;
            fixed (ScenePushConstants* data = &push)
            {
                vk.CmdPushConstants(commandBuffer, pipelineLayout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(ScenePushConstants), data);
            }

            vk.CmdDrawIndexed(commandBuffer, mesh.IndexCount, 1, mesh.FirstIndex, mesh.VertexOffset, 0);
        }
    }

    // Scene colours are authored in sRGB; lighting, blending and bloom happen in linear HDR.
    private static Vector3 SrgbToLinear(Vector3 color) => new(float.Pow(color.X, 2.2f), float.Pow(color.Y, 2.2f), float.Pow(color.Z, 2.2f));

    private void DestroyTarget(ref RenderTarget target)
    {
        if (target.View.Handle == 0)
        {
            return;
        }

        if (target.Framebuffer.Handle != 0)
        {
            vk.DestroyFramebuffer(device, target.Framebuffer, null);
        }

        vk.DestroyImageView(device, target.View, null);
        vk.DestroyImage(device, target.Image, null);
        vk.FreeMemory(device, target.Memory, null);
        target = default;
    }

    private void DestroySwapchainResources(bool keepSwapchain)
    {
        foreach (var framebuffer in framebuffers)
        {
            vk.DestroyFramebuffer(device, framebuffer, null);
        }

        foreach (var view in swapchainViews)
        {
            vk.DestroyImageView(device, view, null);
        }

        foreach (var semaphore in renderFinished)
        {
            vk.DestroySemaphore(device, semaphore, null);
        }

        framebuffers = [];
        swapchainViews = [];
        renderFinished = [];
        DestroyTarget(ref depth);
        DestroyTarget(ref hdr);
        for (var level = 0; level < BloomLevels; level++)
        {
            DestroyTarget(ref bloom[level]);
        }

#if DEBUG
        capturer.Destroy(vk, device);
#endif
        if (!keepSwapchain && swapchain.Handle != 0)
        {
            khrSwapchain.DestroySwapchain(device, swapchain, null);
            swapchain = default;
        }
    }

    private void Cleanup()
    {
        if (vk is null)
        {
            return;
        }

        if (device.Handle != 0)
        {
            vk.DeviceWaitIdle(device);
            if (khrSwapchain is not null)
            {
                DestroySwapchainResources(keepSwapchain: false);
            }

            vk.DestroyFence(device, inFlight, null);
            vk.DestroySemaphore(device, imageAvailable, null);
            vk.DestroyCommandPool(device, commandPool, null);
            vk.DestroyBuffer(device, vertexBuffer, null);
            vk.FreeMemory(device, vertexMemory, null);
            vk.DestroyBuffer(device, indexBuffer, null);
            vk.FreeMemory(device, indexMemory, null);
            foreach (var pipeline in (ReadOnlySpan<Pipeline>)[opaquePipeline, glowPipeline, surfacePipeline, downPipeline, upPipeline, compositePipeline])
            {
                vk.DestroyPipeline(device, pipeline, null);
            }

            vk.DestroyPipelineLayout(device, pipelineLayout, null);
            vk.DestroyPipelineLayout(device, postLayout, null);
            vk.DestroyDescriptorPool(device, descriptorPool, null);
            vk.DestroyDescriptorSetLayout(device, postSetLayout, null);
            vk.DestroySampler(device, sampler, null);
            foreach (var pass in (ReadOnlySpan<RenderPass>)[scenePass, downPass, upPass, compositePass])
            {
                vk.DestroyRenderPass(device, pass, null);
            }

            vk.DestroyDevice(device, null);
        }

        if (instance.Handle != 0)
        {
            if (surface.Handle != 0)
            {
                khrSurface?.DestroySurface(instance, surface, null);
            }

            vk.DestroyInstance(instance, null);
        }

        vk.Dispose();
    }

    private static void Check(Result result, string action, [CallerMemberName] string caller = "")
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan could not {action} ({result}) in {caller}.");
        }
    }

    private readonly record struct RenderTarget(VkImage Image, DeviceMemory Memory, ImageView View, Framebuffer Framebuffer, Extent2D Extent);
}