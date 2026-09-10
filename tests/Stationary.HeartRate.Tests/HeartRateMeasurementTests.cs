using Stationary.HeartRate;

namespace Stationary.HeartRate.Tests;

public sealed class HeartRateMeasurementTests
{
    [Test]
    public async Task ServiceIds_UseTheBluetoothBaseUuid()
    {
        using (Assert.Multiple())
        {
            await Assert.That(HeartRateService.Id).IsEqualTo(new Guid("0000180d-0000-1000-8000-00805f9b34fb"));
            await Assert.That(HeartRateService.MeasurementCharacteristicId).IsEqualTo(new Guid("00002a37-0000-1000-8000-00805f9b34fb"));
            await Assert.That(HeartRateService.BodySensorLocationCharacteristicId).IsEqualTo(new Guid("00002a38-0000-1000-8000-00805f9b34fb"));
            await Assert.That(HeartRateService.ControlPointCharacteristicId).IsEqualTo(new Guid("00002a39-0000-1000-8000-00805f9b34fb"));
        }
    }

    [Test]
    public async Task Measurement_DecodesEightBitHeartRate()
    {
        var status = HeartRateMeasurement.TryDecode([0x00, 72], out var measurement, out var rrIntervalData, HeartRateValidationMode.Strict);
        var rrIntervalDataLength = rrIntervalData.Length;

        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(HeartRateDecodeStatus.Success);
            await Assert.That(measurement).IsEqualTo(new HeartRateMeasurement(72, false, HeartRateSensorContactStatus.NotSupported, null, 0));
            await Assert.That(rrIntervalDataLength).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Measurement_DecodesOptionalFieldsAndRrIntervals()
    {
        HeartRateDecodeStatus status;
        HeartRateMeasurement measurement;
        int rrIntervalDataLength;
        bool hasFirstInterval;
        ushort firstInterval;
        bool hasSecondInterval;
        ushort secondInterval;
        {
            status = HeartRateMeasurement.TryDecode([0x1F, 0x2C, 0x01, 0x58, 0x02, 0x00, 0x04, 0x00, 0x02], out measurement, out var rrIntervalData, HeartRateValidationMode.Strict);
            rrIntervalDataLength = rrIntervalData.Length;
            hasFirstInterval = HeartRateMeasurement.TryGetRrInterval(rrIntervalData, 0, out firstInterval);
            hasSecondInterval = HeartRateMeasurement.TryGetRrInterval(rrIntervalData, 1, out secondInterval);
        }

        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(HeartRateDecodeStatus.Success);
            await Assert.That(measurement).IsEqualTo(new HeartRateMeasurement(300, true, HeartRateSensorContactStatus.Detected, 600, 2));
            await Assert.That(rrIntervalDataLength).IsEqualTo(4);
            await Assert.That(hasFirstInterval).IsTrue();
            await Assert.That(firstInterval).IsEqualTo((ushort)1024);
            await Assert.That(hasSecondInterval).IsTrue();
            await Assert.That(secondInterval).IsEqualTo((ushort)512);
            await Assert.That(HeartRateMeasurement.GetRrIntervalSeconds(firstInterval)).IsEqualTo(1d);
        }
    }

    [Test]
    public async Task Measurement_RejectsTruncatedVariableLengthFields()
    {
        var truncatedHeartRate = HeartRateMeasurement.TryDecode([0x01, 0x72], out _, out _);
        var truncatedEnergy = HeartRateMeasurement.TryDecode([0x08, 72, 0x10], out _, out _);
        var truncatedRrInterval = HeartRateMeasurement.TryDecode([0x10, 72, 0x00], out _, out _);

        using (Assert.Multiple())
        {
            await Assert.That(truncatedHeartRate).IsEqualTo(HeartRateDecodeStatus.InsufficientData);
            await Assert.That(truncatedEnergy).IsEqualTo(HeartRateDecodeStatus.InsufficientData);
            await Assert.That(truncatedRrInterval).IsEqualTo(HeartRateDecodeStatus.InsufficientData);
        }
    }

    [Test]
    public async Task Measurement_IgnoresReservedFlagsAndEnforcesStrictTrailingData()
    {
        var reservedFlags = HeartRateMeasurement.TryDecode([0xE0, 72], out var reservedMeasurement, out _);
        var compatible = HeartRateMeasurement.TryDecode([0x00, 72, 0xFF], out _, out _);
        var strict = HeartRateMeasurement.TryDecode([0x00, 72, 0xFF], out _, out _, HeartRateValidationMode.Strict);

        using (Assert.Multiple())
        {
            await Assert.That(reservedFlags).IsEqualTo(HeartRateDecodeStatus.Success);
            await Assert.That(reservedMeasurement.SensorContactStatus).IsEqualTo(HeartRateSensorContactStatus.NotSupported);
            await Assert.That(compatible).IsEqualTo(HeartRateDecodeStatus.Success);
            await Assert.That(strict).IsEqualTo(HeartRateDecodeStatus.TrailingData);
        }
    }
}