using System.Buffers.Binary;

namespace Stationary.Ftms;

public enum FtmsControlPointOpcode : byte
{
    RequestControl, Reset, SetTargetSpeed, SetTargetInclination, SetTargetResistanceLevel, SetTargetPower, SetTargetHeartRate,
    StartOrResume, StopOrPause, SetTargetedExpendedEnergy, SetTargetedSteps, SetTargetedStrides, SetTargetedDistance,
    SetTargetedTrainingTime, SetTargetedTimeInTwoHeartRateZones, SetTargetedTimeInThreeHeartRateZones, SetTargetedTimeInFiveHeartRateZones,
    SetIndoorBikeSimulationParameters, SetWheelCircumference, SpinDownControl, SetTargetedCadence, ResponseCode = 0x80,
}

public enum FtmsControlPointResult : byte { Success = 1, OpCodeNotSupported, InvalidParameter, OperationFailed, ControlNotPermitted }

public readonly record struct FitnessMachineControlPoint(FtmsControlPointOpcode Opcode, ReadOnlyMemory<byte> Parameters)
{
    public static FtmsDecodeStatus TryDecode(ReadOnlySpan<byte> source, out FtmsControlPointOpcode opcode, out ReadOnlySpan<byte> parameters, FtmsValidationMode validationMode = FtmsValidationMode.Compatible)
    {
        opcode = default;
        parameters = default;
        if (source.IsEmpty) return FtmsDecodeStatus.InsufficientData;
        opcode = (FtmsControlPointOpcode)source[0];
        var parameterLength = GetParameterLength(opcode, source[1..]);
        if (parameterLength < 0 || source.Length - 1 < parameterLength) return FtmsDecodeStatus.InvalidFlags;
        if (validationMode == FtmsValidationMode.Strict && source.Length != parameterLength + 1) return FtmsDecodeStatus.TrailingData;
        parameters = source.Slice(1, parameterLength);
        return FtmsDecodeStatus.Success;
    }

    public static bool TryEncode(FtmsControlPointOpcode opcode, ReadOnlySpan<byte> parameters, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        var length = GetParameterLength(opcode, parameters);
        if (length < 0 || parameters.Length != length || destination.Length < length + 1) return false;
        destination[0] = (byte)opcode;
        parameters.CopyTo(destination[1..]);
        bytesWritten = length + 1;
        return true;
    }

    public static bool TryEncodeResponse(FtmsControlPointOpcode requestOpcode, FtmsControlPointResult result, ReadOnlySpan<byte> responseParameters, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        var expectedLength = requestOpcode == FtmsControlPointOpcode.SpinDownControl && result == FtmsControlPointResult.Success ? 4 : 0;
        if (responseParameters.Length != expectedLength || destination.Length < 3 + expectedLength) return false;
        destination[0] = (byte)FtmsControlPointOpcode.ResponseCode;
        destination[1] = (byte)requestOpcode;
        destination[2] = (byte)result;
        responseParameters.CopyTo(destination[3..]);
        bytesWritten = 3 + expectedLength;
        return true;
    }

    private static int GetParameterLength(FtmsControlPointOpcode opcode, ReadOnlySpan<byte> parameters) => opcode switch
    {
        FtmsControlPointOpcode.RequestControl or FtmsControlPointOpcode.Reset or FtmsControlPointOpcode.StartOrResume => 0,
        FtmsControlPointOpcode.SetTargetResistanceLevel or FtmsControlPointOpcode.SetTargetHeartRate or FtmsControlPointOpcode.StopOrPause or FtmsControlPointOpcode.SpinDownControl => 1,
        FtmsControlPointOpcode.SetTargetSpeed or FtmsControlPointOpcode.SetTargetInclination or FtmsControlPointOpcode.SetTargetPower or FtmsControlPointOpcode.SetTargetedExpendedEnergy or FtmsControlPointOpcode.SetTargetedSteps or FtmsControlPointOpcode.SetTargetedStrides or FtmsControlPointOpcode.SetTargetedTrainingTime or FtmsControlPointOpcode.SetWheelCircumference or FtmsControlPointOpcode.SetTargetedCadence => 2,
        FtmsControlPointOpcode.SetTargetedDistance => 3,
        FtmsControlPointOpcode.SetTargetedTimeInTwoHeartRateZones => 4,
        FtmsControlPointOpcode.SetTargetedTimeInThreeHeartRateZones or FtmsControlPointOpcode.SetIndoorBikeSimulationParameters => 6,
        FtmsControlPointOpcode.SetTargetedTimeInFiveHeartRateZones => 10,
        FtmsControlPointOpcode.ResponseCode => parameters.Length >= 2 && parameters[1] == (byte)FtmsControlPointResult.Success && parameters[0] == (byte)FtmsControlPointOpcode.SpinDownControl ? 6 : 2,
        _ => -1,
    };
}