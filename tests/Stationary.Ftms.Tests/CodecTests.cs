using Stationary.Ftms;

namespace Stationary.Ftms.Tests;

public sealed class CodecTests
{
    [Test]
    public async Task FitnessMachineFeature_DecodesLittleEndianBitFields()
    {
        var status = FitnessMachineFeature.TryDecode([0x82, 0x40, 0x01, 0x00, 0x05, 0x01, 0x00, 0x00], out var value);

        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(value.SupportsCadence).IsTrue();
            await Assert.That(value.SupportsResistanceLevel).IsTrue();
            await Assert.That(value.SupportsPowerMeasurement).IsTrue();
            await Assert.That(value.TargetSettingFeatures).IsEqualTo(0x105u);
        }
    }

    [Test]
    public async Task FitnessMachineFeature_PreservesAllDefinedTargetSettingFeatures()
    {
        const uint targetSettingFeatures = 0x0001_FFFF;
        Span<byte> encoded = stackalloc byte[FitnessMachineFeature.EncodedLength];
        var original = new FitnessMachineFeature(0, targetSettingFeatures);

        var encodedSuccessfully = original.TryEncode(encoded, out var bytesWritten);
        var status = FitnessMachineFeature.TryDecode(encoded[..bytesWritten], out var decoded, FtmsValidationMode.Strict);

        await Assert.That(encodedSuccessfully).IsTrue();
        await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
        await Assert.That(decoded.TargetSettingFeatures).IsEqualTo(targetSettingFeatures);

        using (Assert.Multiple())
        {
            await Assert.That(decoded.SupportsTargetSpeed).IsTrue();
            await Assert.That(decoded.SupportsTargetInclination).IsTrue();
            await Assert.That(decoded.SupportsTargetResistanceLevel).IsTrue();
            await Assert.That(decoded.SupportsTargetPower).IsTrue();
            await Assert.That(decoded.SupportsTargetHeartRate).IsTrue();
            await Assert.That(decoded.SupportsTargetedExpendedEnergy).IsTrue();
            await Assert.That(decoded.SupportsTargetedStepNumber).IsTrue();
            await Assert.That(decoded.SupportsTargetedStrideNumber).IsTrue();
            await Assert.That(decoded.SupportsTargetedDistance).IsTrue();
            await Assert.That(decoded.SupportsTargetedTrainingTime).IsTrue();
            await Assert.That(decoded.SupportsTargetedTimeInTwoHeartRateZones).IsTrue();
            await Assert.That(decoded.SupportsTargetedTimeInThreeHeartRateZones).IsTrue();
            await Assert.That(decoded.SupportsTargetedTimeInFiveHeartRateZones).IsTrue();
            await Assert.That(decoded.SupportsIndoorBikeSimulationParameters).IsTrue();
            await Assert.That(decoded.SupportsWheelCircumference).IsTrue();
            await Assert.That(decoded.SupportsSpinDownControl).IsTrue();
            await Assert.That(decoded.SupportsTargetedCadence).IsTrue();
        }
    }

    [Test]
    public async Task IndoorBikeData_RoundTripsFlagOrderedFields()
    {
        var original = new IndoorBikeData(false, 2_573, 2_500, 180, null, 12_345, -12, 245, null, 400, 600, 10, 152, 87, 1_234, null);
        var encoded = new byte[original.GetEncodedLength()];

        var encodedSuccessfully = original.TryEncode(encoded, out var bytesWritten);
        var status = IndoorBikeData.TryDecode(encoded[..bytesWritten], out var decoded, FtmsValidationMode.Strict);

        using (Assert.Multiple())
        {
            await Assert.That(encodedSuccessfully).IsTrue();
            await Assert.That(bytesWritten).IsEqualTo(encoded.Length);
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(decoded).IsEqualTo(original);
            await Assert.That(decoded.InstantaneousSpeedKilometersPerHour).IsEqualTo(25.73d);
            await Assert.That(decoded.InstantaneousCadenceRpm).IsEqualTo(90d);
        }
    }

    [Test]
    public async Task IndoorBikeData_RejectsTruncatedPayload()
    {
        var status = IndoorBikeData.TryDecode([0x00, 0x00, 0x01], out _);

        await Assert.That(status).IsEqualTo(FtmsDecodeStatus.InsufficientData);
    }

    [Test]
    public async Task ServiceData_RoundTripsAssignedNumberPayload()
    {
        var original = new FtmsServiceData(true, 1 << 5);
        Span<byte> destination = stackalloc byte[FtmsServiceData.EncodedLength];
        var encoded = original.TryEncode(destination, out var bytesWritten);
        var status = FtmsServiceData.TryDecode(destination[..bytesWritten], out var decoded, FtmsValidationMode.Strict);

        using (Assert.Multiple())
        {
            await Assert.That(encoded).IsTrue();
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(decoded).IsEqualTo(original);
        }
    }

    [Test]
    public async Task SupportedRange_DecodesLittleEndianValues()
    {
        var status = FtmsInt16Range.TryDecode([0xF6, 0xFF, 0x64, 0x00, 0x05, 0x00], out var range, FtmsValidationMode.Strict);

        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(range).IsEqualTo(new FtmsInt16Range(-10, 100, 5));
        }
    }

    [Test]
    public async Task SupportedRanges_DecodeUnsignedAndByteValues()
    {
        var unsignedStatus = FtmsUInt16Range.TryDecode([0x64, 0x00, 0xC8, 0x00, 0x05, 0x00], out var unsignedRange, FtmsValidationMode.Strict);
        var byteStatus = FtmsByteRange.TryDecode([0x02, 0x20, 0x02], out var byteRange, FtmsValidationMode.Strict);

        using (Assert.Multiple())
        {
            await Assert.That(unsignedStatus).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(unsignedRange).IsEqualTo(new FtmsUInt16Range(100, 200, 5));
            await Assert.That(byteStatus).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(byteRange).IsEqualTo(new FtmsByteRange(2, 32, 2));
        }
    }

    [Test]
    public async Task ControlPoint_EncodesAndDecodesSimulationParameters()
    {
        Span<byte> payload = stackalloc byte[7];
        var encoded = FitnessMachineControlPoint.TryEncode(FtmsControlPointOpcode.SetIndoorBikeSimulationParameters, [0x10, 0x00, 0xFE, 0xFF, 0x05, 0x06], payload, out var bytesWritten);
        var status = FitnessMachineControlPoint.TryDecode(payload[..bytesWritten], out var opcode, out var parameters, FtmsValidationMode.Strict);
        var parameterLength = parameters.Length;

        using (Assert.Multiple())
        {
            await Assert.That(encoded).IsTrue();
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(opcode).IsEqualTo(FtmsControlPointOpcode.SetIndoorBikeSimulationParameters);
            await Assert.That(parameterLength).IsEqualTo(6);
        }
    }

    [Test]
    [Arguments(FtmsMachineDataType.Treadmill, new byte[] { 0x00, 0x00, 0xE8, 0x03 })]
    [Arguments(FtmsMachineDataType.CrossTrainer, new byte[] { 0x00, 0x00, 0xE8, 0x03 })]
    [Arguments(FtmsMachineDataType.StepClimber, new byte[] { 0x00, 0x00, 0x01, 0x00, 0x02, 0x00 })]
    [Arguments(FtmsMachineDataType.StairClimber, new byte[] { 0x00, 0x00, 0x01, 0x00 })]
    [Arguments(FtmsMachineDataType.Rower, new byte[] { 0x00, 0x00, 0x20, 0x02, 0x00 })]
    public async Task MachineData_ValidatesMandatoryFields(FtmsMachineDataType type, byte[] payload)
    {
        var status = FtmsMachineData.TryValidate(type, payload, FtmsValidationMode.Strict);

        await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
    }

    [Test]
    public async Task MachineStatus_DecodesTargetedDistance()
    {
        var status = FitnessMachineStatus.TryDecode([0x0D, 0x56, 0x34, 0x12], out var opcode, out var parameters, FtmsValidationMode.Strict);
        var parameterLength = parameters.Length;

        using (Assert.Multiple())
        {
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(opcode).IsEqualTo(FtmsMachineStatusOpcode.TargetedDistanceChanged);
            await Assert.That(parameterLength).IsEqualTo(3);
        }
    }

    [Test]
    public async Task MachineStatus_DecodesUserPauseAndResume()
    {
        var pauseStatus = FitnessMachineStatus.TryDecode([0x02, 0x02], out var pauseOpcode, out var pauseParameters, FtmsValidationMode.Strict);
        var pauseControlInformation = pauseParameters[0];
        var resumeStatus = FitnessMachineStatus.TryDecode([0x04], out var resumeOpcode, out var resumeParameters, FtmsValidationMode.Strict);
        var resumeParameterLength = resumeParameters.Length;

        using (Assert.Multiple())
        {
            await Assert.That(pauseStatus).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(pauseOpcode).IsEqualTo(FtmsMachineStatusOpcode.StoppedOrPausedByUser);
            await Assert.That(pauseControlInformation).IsEqualTo((byte)0x02);
            await Assert.That(resumeStatus).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(resumeOpcode).IsEqualTo(FtmsMachineStatusOpcode.StartedOrResumedByUser);
            await Assert.That(resumeParameterLength).IsEqualTo(0);
        }
    }

    [Test]
    public async Task TreadmillData_RoundTripsFlagOrderedFields()
    {
        var original = new TreadmillData(false, 1_250, 1_200, 12_345, -10, 125, 42, 3, 300, 310, 400, 500, 8, 150, 95, 600, 60, -120, 250);
        var encoded = new byte[original.GetEncodedLength()];

        var wasEncoded = original.TryEncode(encoded, out var bytesWritten);
        var status = TreadmillData.TryDecode(encoded[..bytesWritten], out var decoded, FtmsValidationMode.Strict);

        using (Assert.Multiple())
        {
            await Assert.That(wasEncoded).IsTrue();
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(decoded).IsEqualTo(original);
        }
    }

    [Test]
    public async Task CrossTrainerData_RoundTripsFlagOrderedFields()
    {
        var original = new CrossTrainerData(false, true, 1_250, 1_200, 12_345, 90, 88, 1_000, 42, 3, -10, 125, -4, 250, 245, 400, 500, 8, 150, 95, 600, 60);
        var encoded = new byte[original.GetEncodedLength()];

        var wasEncoded = original.TryEncode(encoded, out var bytesWritten);
        var status = CrossTrainerData.TryDecode(encoded[..bytesWritten], out var decoded, FtmsValidationMode.Strict);

        using (Assert.Multiple())
        {
            await Assert.That(wasEncoded).IsTrue();
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(decoded).IsEqualTo(original);
        }
    }

    [Test]
    public async Task StepClimberData_RoundTripsFlagOrderedFields()
    {
        var original = new StepClimberData(false, 120, 4_000, 90, 88, 42, 400, 500, 8, 150, 95, 600, 60);
        var encoded = new byte[original.GetEncodedLength()];

        var wasEncoded = original.TryEncode(encoded, out var bytesWritten);
        var status = StepClimberData.TryDecode(encoded[..bytesWritten], out var decoded, FtmsValidationMode.Strict);

        using (Assert.Multiple())
        {
            await Assert.That(wasEncoded).IsTrue();
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(decoded).IsEqualTo(original);
        }
    }

    [Test]
    public async Task StairClimberData_RoundTripsFlagOrderedFields()
    {
        var original = new StairClimberData(false, 120, 90, 88, 42, 1_000, 400, 500, 8, 150, 95, 600, 60);
        var encoded = new byte[original.GetEncodedLength()];

        var wasEncoded = original.TryEncode(encoded, out var bytesWritten);
        var status = StairClimberData.TryDecode(encoded[..bytesWritten], out var decoded, FtmsValidationMode.Strict);

        using (Assert.Multiple())
        {
            await Assert.That(wasEncoded).IsTrue();
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(decoded).IsEqualTo(original);
        }
    }

    [Test]
    public async Task RowerData_RoundTripsFlagOrderedFields()
    {
        var original = new RowerData(false, 30, 1_000, 28, 12_345, 120, 125, 250, 245, -4, 400, 500, 8, 150, 95, 600, 60);
        var encoded = new byte[original.GetEncodedLength()];

        var wasEncoded = original.TryEncode(encoded, out var bytesWritten);
        var status = RowerData.TryDecode(encoded[..bytesWritten], out var decoded, FtmsValidationMode.Strict);

        using (Assert.Multiple())
        {
            await Assert.That(wasEncoded).IsTrue();
            await Assert.That(status).IsEqualTo(FtmsDecodeStatus.Success);
            await Assert.That(decoded).IsEqualTo(original);
        }
    }
}