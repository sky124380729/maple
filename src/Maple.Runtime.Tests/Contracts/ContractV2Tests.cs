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
