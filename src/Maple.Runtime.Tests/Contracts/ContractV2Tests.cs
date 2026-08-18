using Maple.Contracts;
using Xunit;

namespace Maple.Runtime.Tests.Contracts;

public sealed class ContractV2Tests
{
    [Fact]
    public void RuntimeUsesContractVersionTwo()
    {
        Assert.Equal(2, ContractConstants.SchemaVersion);
    }

    [Fact]
    public void ActionVocabularyDoesNotExposeDirectionalInputAliases()
    {
        string[] names = Enum.GetNames<ActionType>();

        Assert.DoesNotContain("MoveUp", names);
        Assert.DoesNotContain("MoveDown", names);
    }

    [Fact]
    public void ActionsExposeAnExplicitProfile()
    {
        Assert.NotNull(typeof(AbstractAction).GetProperty("ProfileId"));
    }

    [Fact]
    public void AttackRequiresACompatibleProfile()
    {
        var missingProfile = new AbstractAction
        {
            ActionId = "attack-missing-profile",
            Type = ActionType.Attack,
            HoldMs = 80,
            MaxDurationMs = 200
        };
        var validAttack = new AbstractAction
        {
            ActionId = "attack-profile",
            Type = ActionType.Attack,
            HoldMs = 80,
            MaxDurationMs = 200
        };
        var profileProperty = typeof(AbstractAction).GetProperty("ProfileId");
        Assert.NotNull(profileProperty);
        Type profileType = Nullable.GetUnderlyingType(profileProperty.PropertyType) ?? profileProperty.PropertyType;
        profileProperty.SetValue(validAttack, Enum.Parse(profileType, "SingleAttack"));

        Assert.False(ContractValidation.ValidateAction(missingProfile).IsValid);
        Assert.True(ContractValidation.ValidateAction(validAttack).IsValid);
    }

    [Fact]
    public void StationaryAttackAllowsThirtySecondHold()
    {
        var attack = new AbstractAction
        {
            ActionId = "stationary-attack",
            Type = ActionType.Attack,
            ProfileId = ActionProfileId.SingleAttack,
            HoldMs = 30_000,
            MaxDurationMs = 30_000
        };

        Assert.True(ContractValidation.ValidateAction(attack).IsValid);
    }

    [Fact]
    public void MovementKeepsTheShortActionLimit()
    {
        var movement = new AbstractAction
        {
            ActionId = "too-long-movement",
            Type = ActionType.MoveLeft,
            HoldMs = 5_001,
            MaxDurationMs = 5_001
        };

        Assert.False(ContractValidation.ValidateAction(movement).IsValid);
    }

    [Fact]
    public void ContractPublishesTheBailianBridgeMessages()
    {
        string[] commands = Enum.GetNames<UiCommandType>();
        string[] events = Enum.GetNames<HostEventType>();

        Assert.Contains("CloudCredentialSet", commands);
        Assert.Contains("CloudCredentialClear", commands);
        Assert.Contains("CloudConfigUpdate", commands);
        Assert.Contains("CloudConnectionTest", commands);
        Assert.Contains("CloudMapAnnotate", commands);
        Assert.Contains("CloudStatusUpdated", events);
    }
}
