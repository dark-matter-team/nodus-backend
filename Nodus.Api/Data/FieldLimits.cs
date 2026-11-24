namespace Nodus.Api.Data;

/// <summary>
/// Column length limits shared by the EF model (database constraints) and service validation.
/// </summary>
public static class FieldLimits
{
    public const int Email = 254;
    public const int Nickname = 50;
    public const int GoogleId = 255;
    public const int PasswordResetCode = 6;
    public const int Title = 200;
    public const int GoalPrompt = 500;
    public const int TimeCommitment = 50;
    public const int EnumValue = 20;
}
