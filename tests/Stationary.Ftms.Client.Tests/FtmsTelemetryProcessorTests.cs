using Stationary.Ftms.Client;
using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Client.Tests;

public sealed class FtmsTelemetryProcessorTests
{
    [Test]
    public async Task Process_MergesOptionalFieldsFromFragmentedIndoorBikeData()
    {
        var buffer = new LatestTelemetryBuffer();
        var processor = new FtmsTelemetryProcessor(FtmsMachineDataType.IndoorBike, buffer);

        var firstStatus = processor.Process([0x00, 0x00, 0xC4, 0x09], DateTimeOffset.UnixEpoch);
        var secondStatus = processor.Process([0x45, 0x02, 0xFA, 0x00, 0xB4, 0x00, 0x96], DateTimeOffset.UnixEpoch.AddSeconds(1));
        var snapshot = await buffer.Reader.ReadAsync(CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(firstStatus).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(secondStatus).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(snapshot.SpeedKilometersPerHour).IsEqualTo(25d);
            await Assert.That(snapshot.CadenceRpm).IsEqualTo(125d);
            await Assert.That(snapshot.PowerWatts).IsEqualTo((short)180);
            await Assert.That(snapshot.HeartRateBeatsPerMinute).IsEqualTo((byte)150);
            await Assert.That(snapshot.ReportedFields.HasFlag(TelemetryField.TotalDistance)).IsFalse();
        }
    }

    [Test]
    public async Task Process_IdentifiesDistanceReportedByCurrentIndoorBikePacket()
    {
        var buffer = new LatestTelemetryBuffer();
        var processor = new FtmsTelemetryProcessor(FtmsMachineDataType.IndoorBike, buffer);

        var status = processor.Process([0x10, 0x00, 0x00, 0x00, 0x34, 0x12, 0x00], DateTimeOffset.UnixEpoch);
        var snapshot = await buffer.Reader.ReadAsync(CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(snapshot.TotalDistanceMeters).IsEqualTo(4_660u);
            await Assert.That(snapshot.ReportedFields.HasFlag(TelemetryField.TotalDistance)).IsTrue();
        }
    }

    [Test]
    public async Task Process_NormalizesUnavailableEnergyValuesAndClearsPreviousValues()
    {
        var buffer = new LatestTelemetryBuffer();
        var processor = new FtmsTelemetryProcessor(FtmsMachineDataType.IndoorBike, buffer);

        processor.Process([0x00, 0x01, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x03], DateTimeOffset.UnixEpoch);
        var status = processor.Process([0x00, 0x01, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], DateTimeOffset.UnixEpoch.AddSeconds(1));
        var snapshot = await buffer.Reader.ReadAsync(CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(snapshot.ReportedFields.HasFlag(TelemetryField.TotalEnergy)).IsTrue();
            await Assert.That(snapshot.TotalEnergyKilocalories).IsNull();
            await Assert.That(snapshot.EnergyPerHourKilocalories).IsNull();
            await Assert.That(snapshot.EnergyPerMinuteKilocalories).IsNull();
        }
    }

    [Test]
    public async Task Process_RejectsInvalidPayloadWithoutPublishing()
    {
        var buffer = new LatestTelemetryBuffer();
        var processor = new FtmsTelemetryProcessor(FtmsMachineDataType.Rower, buffer);

        var status = processor.Process([0x00], DateTimeOffset.UnixEpoch);

        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.InsufficientData);
            await Assert.That(buffer.Reader.TryRead(out _)).IsFalse();
        }
    }

    [Test]
    public async Task ExecuteAsync_ResolvesMatchingControlPointIndication()
    {
        var transport = new FakeControlPointTransport();
        using var coordinator = new FtmsControlPointCoordinator(transport);
        transport.Written = payload => coordinator.HandleIndication([0x80, payload.Span[0], 0x01]);

        var result = await coordinator.ExecuteAsync(FtmsControlPointOpcode.RequestControl, Array.Empty<byte>(), TimeSpan.FromSeconds(1));

        await Assert.That(result).IsEqualTo(FtmsControlPointResult.Success);
    }

    [Test]
    public async Task Process_ReportsStepAndStairClimberFields()
    {
        var stepData = new StepClimberData(false, 120, 4_000, 90, 88, 42, 400, 500, 8, 150, 95, 600, 60);
        var stairData = new StairClimberData(false, 120, 90, 88, 42, 1_000, 400, 500, 8, 150, 95, 600, 60);
        var stepPayload = new byte[stepData.GetEncodedLength()];
        var stairPayload = new byte[stairData.GetEncodedLength()];
        stepData.TryEncode(stepPayload, out _);
        stairData.TryEncode(stairPayload, out _);
        var stepBuffer = new LatestTelemetryBuffer();
        var stairBuffer = new LatestTelemetryBuffer();

        new FtmsTelemetryProcessor(FtmsMachineDataType.StepClimber, stepBuffer).Process(stepPayload, DateTimeOffset.UnixEpoch);
        new FtmsTelemetryProcessor(FtmsMachineDataType.StairClimber, stairBuffer).Process(stairPayload, DateTimeOffset.UnixEpoch);
        var stepSnapshot = await stepBuffer.Reader.ReadAsync();
        var stairSnapshot = await stairBuffer.Reader.ReadAsync();

        using (Assert.Multiple())
        {
            await Assert.That(stepSnapshot.ReportedFields.HasFlag(TelemetryField.StepCount)).IsTrue();
            await Assert.That(stepSnapshot.ReportedFields.HasFlag(TelemetryField.Cadence)).IsTrue();
            await Assert.That(stairSnapshot.ReportedFields.HasFlag(TelemetryField.StrideCount)).IsTrue();
            await Assert.That(stairSnapshot.ReportedFields.HasFlag(TelemetryField.Cadence)).IsTrue();
        }
    }

    [Test]
    public async Task Dispose_CancelsActiveControlPointRequest()
    {
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeControlPointTransport { Written = _ => writeStarted.TrySetResult() };
        var coordinator = new FtmsControlPointCoordinator(transport);
        var execution = coordinator.ExecuteAsync(FtmsControlPointOpcode.RequestControl, Array.Empty<byte>(), TimeSpan.FromMinutes(1)).AsTask();
        await writeStarted.Task;

        coordinator.Dispose();
        try
        {
            await execution;
        }
        catch (OperationCanceledException)
        {
        }

        await Assert.That(execution.IsCanceled).IsTrue();
    }

    private sealed class FakeControlPointTransport : IFtmsControlPointTransport
    {
        public Func<ReadOnlyMemory<byte>, bool>? Written { get; set; }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            Written?.Invoke(payload);
            return ValueTask.CompletedTask;
        }
    }
}