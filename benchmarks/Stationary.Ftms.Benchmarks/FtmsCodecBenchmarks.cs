using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;

using Stationary.Ftms;

namespace Stationary.Ftms.Benchmarks;

[MemoryDiagnoser(displayGenColumns: true)]
[ExceptionDiagnoser]
[ThreadingDiagnoser]
[MinColumn]
[MaxColumn]
[MedianColumn]
[RankColumn]
public class FtmsCodecBenchmarks
{
    private static readonly byte[] MinimalIndoorBikePayload = [0x00, 0x00, 0x0D, 0x0A];
    private static readonly byte[] FullIndoorBikePayload = [0xFE, 0x1F, 0x0D, 0x0A, 0xC4, 0x09, 0xB4, 0x00, 0xB0, 0x00, 0x39, 0x30, 0x00, 0xF4, 0xFF, 0xFA, 0x00, 0xF5, 0xFF, 0x90, 0x01, 0x58, 0x02, 0x0A, 0x96, 0x50, 0xD2, 0x04, 0x2C, 0x01];
    private static readonly byte[] TruncatedIndoorBikePayload = [0x00, 0x00, 0x0D];
    private static readonly byte[] FullTreadmillPayload = [0xFE, 0x1F, 0x0D, 0x0A, 0xC4, 0x09, 0x39, 0x30, 0x00, 0x01, 0x00, 0x02, 0x00, 0x0A, 0x00, 0x14, 0x00, 0x1E, 0x00, 0x28, 0x00, 0x32, 0x00, 0x90, 0x01, 0x58, 0x02, 0x0A, 0x96, 0x50, 0xD2, 0x04, 0x2C, 0x01, 0x01, 0x00];
    private static readonly byte[] FitnessMachineFeaturePayload = [0x82, 0x40, 0x01, 0x00, 0x05, 0x01, 0x00, 0x00];
    private static readonly IndoorBikeData FullIndoorBikeData = new(false, 2_573, 2_500, 180, 176, 12_345, -12, 245, 250, 400, 600, 10, 152, 87, 1_234, 300);
    private static readonly FitnessMachineFeature FitnessMachineFeature = new(0x0001_4082, 0x0000_0105);
    private static readonly TreadmillData TreadmillData = new(false, 1_250, 1_200, 12_345, -10, 125, 42, 3, 300, 310, 400, 500, 8, 150, 95, 600, 60, -120, 250);
    private static readonly CrossTrainerData CrossTrainerData = new(false, true, 1_250, 1_200, 12_345, 90, 88, 1_000, 42, 3, -10, 125, -4, 250, 245, 400, 500, 8, 150, 95, 600, 60);
    private static readonly StepClimberData StepClimberData = new(false, 120, 4_000, 90, 88, 42, 400, 500, 8, 150, 95, 600, 60);
    private static readonly StairClimberData StairClimberData = new(false, 120, 90, 88, 42, 1_000, 400, 500, 8, 150, 95, 600, 60);
    private static readonly RowerData RowerData = new(false, 30, 1_000, 28, 12_345, 120, 125, 250, 245, -4, 400, 500, 8, 150, 95, 600, 60);

    private readonly byte[] indoorBikeDestination = new byte[FullIndoorBikeData.GetEncodedLength()];
    private readonly byte[] featureDestination = new byte[FitnessMachineFeature.EncodedLength];
    private readonly byte[] treadmillDestination = new byte[TreadmillData.GetEncodedLength()];
    private readonly byte[] crossTrainerDestination = new byte[CrossTrainerData.GetEncodedLength()];
    private readonly byte[] stepClimberDestination = new byte[StepClimberData.GetEncodedLength()];
    private readonly byte[] stairClimberDestination = new byte[StairClimberData.GetEncodedLength()];
    private readonly byte[] rowerDestination = new byte[RowerData.GetEncodedLength()];

    [GlobalSetup]
    public void ValidatePayloads()
    {
        if (IndoorBikeData.TryDecode(FullIndoorBikePayload, out _, FtmsValidationMode.Strict) != FtmsDecodeStatus.Success ||
            !TreadmillData.TryEncode(treadmillDestination, out _) ||
            !CrossTrainerData.TryEncode(crossTrainerDestination, out _) ||
            !StepClimberData.TryEncode(stepClimberDestination, out _) ||
            !StairClimberData.TryEncode(stairClimberDestination, out _) ||
            !RowerData.TryEncode(rowerDestination, out _) ||
            FtmsMachineData.TryValidate(FtmsMachineDataType.Treadmill, treadmillDestination, FtmsValidationMode.Strict) != FtmsDecodeStatus.Success)
        {
            throw new InvalidOperationException("Benchmark payload must be a valid complete FTMS record.");
        }
    }

    [BenchmarkCategory("IndoorBike")]
    [Benchmark(Baseline = true)]
    public FtmsDecodeStatus DecodeMinimalIndoorBikeData()
        => IndoorBikeData.TryDecode(MinimalIndoorBikePayload, out _, FtmsValidationMode.Strict);

    [BenchmarkCategory("IndoorBike")]
    [Benchmark]
    public FtmsDecodeStatus DecodeFullIndoorBikeData()
        => IndoorBikeData.TryDecode(FullIndoorBikePayload, out _, FtmsValidationMode.Strict);

    [BenchmarkCategory("IndoorBike")]
    [Benchmark]
    public FtmsDecodeStatus DecodeFullIndoorBikeDataCompatible()
        => IndoorBikeData.TryDecode(FullIndoorBikePayload, out _, FtmsValidationMode.Compatible);

    [BenchmarkCategory("IndoorBike")]
    [Benchmark]
    public FtmsDecodeStatus DecodeTruncatedIndoorBikeData()
        => IndoorBikeData.TryDecode(TruncatedIndoorBikePayload, out _, FtmsValidationMode.Strict);

    [BenchmarkCategory("IndoorBike")]
    [Benchmark]
    public int EncodeFullIndoorBikeData()
    {
        return FullIndoorBikeData.TryEncode(indoorBikeDestination, out var bytesWritten) ? bytesWritten : 0;
    }

    [BenchmarkCategory("Feature")]
    [Benchmark]
    public FtmsDecodeStatus DecodeFitnessMachineFeature()
        => FitnessMachineFeature.TryDecode(FitnessMachineFeaturePayload, out _, FtmsValidationMode.Strict);

    [BenchmarkCategory("Feature")]
    [Benchmark]
    public int EncodeFitnessMachineFeature()
    {
        return FitnessMachineFeature.TryEncode(featureDestination, out var bytesWritten) ? bytesWritten : 0;
    }

    [BenchmarkCategory("Treadmill")]
    [Benchmark]
    public FtmsDecodeStatus DecodeTreadmillData()
        => TreadmillData.TryDecode(treadmillDestination, out _, FtmsValidationMode.Strict);

    [BenchmarkCategory("Treadmill")]
    [Benchmark]
    public int EncodeTreadmillData()
        => TreadmillData.TryEncode(treadmillDestination, out var bytesWritten) ? bytesWritten : 0;

    [BenchmarkCategory("CrossTrainer")]
    [Benchmark]
    public FtmsDecodeStatus DecodeCrossTrainerData()
        => CrossTrainerData.TryDecode(crossTrainerDestination, out _, FtmsValidationMode.Strict);

    [BenchmarkCategory("CrossTrainer")]
    [Benchmark]
    public int EncodeCrossTrainerData()
        => CrossTrainerData.TryEncode(crossTrainerDestination, out var bytesWritten) ? bytesWritten : 0;

    [BenchmarkCategory("StepClimber")]
    [Benchmark]
    public FtmsDecodeStatus DecodeStepClimberData()
        => StepClimberData.TryDecode(stepClimberDestination, out _, FtmsValidationMode.Strict);

    [BenchmarkCategory("StepClimber")]
    [Benchmark]
    public int EncodeStepClimberData()
        => StepClimberData.TryEncode(stepClimberDestination, out var bytesWritten) ? bytesWritten : 0;

    [BenchmarkCategory("StairClimber")]
    [Benchmark]
    public FtmsDecodeStatus DecodeStairClimberData()
        => StairClimberData.TryDecode(stairClimberDestination, out _, FtmsValidationMode.Strict);

    [BenchmarkCategory("StairClimber")]
    [Benchmark]
    public int EncodeStairClimberData()
        => StairClimberData.TryEncode(stairClimberDestination, out var bytesWritten) ? bytesWritten : 0;

    [BenchmarkCategory("Rower")]
    [Benchmark]
    public FtmsDecodeStatus DecodeRowerData()
        => RowerData.TryDecode(rowerDestination, out _, FtmsValidationMode.Strict);

    [BenchmarkCategory("Rower")]
    [Benchmark]
    public int EncodeRowerData()
        => RowerData.TryEncode(rowerDestination, out var bytesWritten) ? bytesWritten : 0;

    [BenchmarkCategory("MachineData")]
    [Benchmark]
    public FtmsDecodeStatus ValidateFullTreadmillData()
        => FtmsMachineData.TryValidate(FtmsMachineDataType.Treadmill, FullTreadmillPayload, FtmsValidationMode.Strict);
}