#if DEBUG
using CoreGraphics;

using Silk.NET.Vulkan;

using Stationary.Ftms.Dashboard.Core;

using UIKit;

using Device = Silk.NET.Vulkan.Device;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImage = Silk.NET.Vulkan.Image;

namespace Stationary.Ftms.Dashboard.Scene;

// Debug-only scene preview: STATIONARY_SCENE_DEMO=1 opens the scene window with a synthetic ride, and
// STATIONARY_SCENE_CAPTURE=<directory> also saves rendered frames as PNG files for visual review.
internal static class SceneDemo
{
    public static bool IsEnabled { get; } = Environment.GetEnvironmentVariable("STATIONARY_SCENE_DEMO") == "1";

    public static string? CaptureDirectory { get; } = IsEnabled ? Environment.GetEnvironmentVariable("STATIONARY_SCENE_CAPTURE") : null;

    public static RideSceneFrame CreateFrame(double seconds)
    {
        var distance = 880d + (seconds * 8d);
        var next = double.Ceiling((distance + 1d) / 1000d) * 1000d;
        return new(
            true,
            28.8d + (double.Sin(seconds / 5d) * 2d),
            86d,
            185d,
            (int)(132d + (double.Sin(seconds / 7d) * 6d)),
            RideGameTargetStatus.OnTarget,
            true,
            60d + seconds,
            distance,
            next,
            2,
            5,
            RideGameMedal.Gold,
            60d - (seconds % 60d),
            (int)(seconds / 6d),
            (int)(seconds / 14d),
            RideGameMedal.Gold);
    }
}

internal sealed unsafe class SceneCapture
{
    private const int MaximumFrames = 12;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2.5d);
    private VkBuffer buffer;
    private DeviceMemory memory;
    private int saved;
    private TimeSpan next = TimeSpan.FromSeconds(4d);

    public bool IsSupported { get; set; }

    public bool ShouldCapture(TimeSpan now)
    {
        if (!IsSupported || SceneDemo.CaptureDirectory is null || saved >= MaximumFrames || now < next)
        {
            return false;
        }

        next = now + Interval;
        return true;
    }

    public void Record(Vk vk, Device device, PhysicalDevice physicalDevice, CommandBuffer commands, VkImage image, Extent2D extent)
    {
        if (buffer.Handle == 0)
        {
            var size = (ulong)extent.Width * extent.Height * 4;
            var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = BufferUsageFlags.TransferDstBit, SharingMode = SharingMode.Exclusive };
            vk.CreateBuffer(device, in info, null, out buffer);
            vk.GetBufferMemoryRequirements(device, buffer, out var requirements);
            vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out var properties);
            var wanted = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
            for (var index = 0; index < properties.MemoryTypeCount; index++)
            {
                if ((requirements.MemoryTypeBits & (1u << index)) != 0 && (properties.MemoryTypes[index].PropertyFlags & wanted) == wanted)
                {
                    var allocate = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = requirements.Size, MemoryTypeIndex = (uint)index };
                    vk.AllocateMemory(device, in allocate, null, out memory);
                    break;
                }
            }

            vk.BindBufferMemory(device, buffer, memory, 0);
        }

        var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
        var toTransfer = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.TransferReadBit,
            OldLayout = ImageLayout.PresentSrcKhr,
            NewLayout = ImageLayout.TransferSrcOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = range,
        };
        vk.CmdPipelineBarrier(commands, PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, in toTransfer);
        var region = new BufferImageCopy
        {
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new Extent3D(extent.Width, extent.Height, 1),
        };
        vk.CmdCopyImageToBuffer(commands, image, ImageLayout.TransferSrcOptimal, buffer, 1, in region);
        var toPresent = toTransfer with
        {
            SrcAccessMask = AccessFlags.TransferReadBit,
            DstAccessMask = 0,
            OldLayout = ImageLayout.TransferSrcOptimal,
            NewLayout = ImageLayout.PresentSrcKhr,
        };
        vk.CmdPipelineBarrier(commands, PipelineStageFlags.TransferBit, PipelineStageFlags.BottomOfPipeBit, 0, 0, null, 0, null, 1, in toPresent);
    }

    public void Save(Vk vk, Device device, Extent2D extent)
    {
        if (SceneDemo.CaptureDirectory is not { } directory || memory.Handle == 0)
        {
            return;
        }

        void* mapped;
        vk.MapMemory(device, memory, 0, (ulong)extent.Width * extent.Height * 4, 0, &mapped);
        try
        {
            Directory.CreateDirectory(directory);
            using var colorSpace = CGColorSpace.CreateSrgb();
            using var context = new CGBitmapContext((nint)mapped, (nint)extent.Width, (nint)extent.Height, 8, (nint)(extent.Width * 4), colorSpace, (CGImageAlphaInfo)(CGBitmapFlags.ByteOrder32Little | CGBitmapFlags.NoneSkipFirst));
            using var image = context.ToImage();
            using var picture = UIImage.FromImage(image!);
            picture.AsPNG()?.Save(Path.Combine(directory, $"scene-{saved:D2}.png"), true, out _);
            saved++;
        }
        finally
        {
            vk.UnmapMemory(device, memory);
        }
    }

    public void Destroy(Vk vk, Device device)
    {
        if (buffer.Handle != 0)
        {
            vk.DestroyBuffer(device, buffer, null);
            vk.FreeMemory(device, memory, null);
            buffer = default;
            memory = default;
        }
    }
}
#endif