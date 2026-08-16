using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Maple.InputBroker;

public static class BrokerPipeSecurity
{
    public static PipeSecurity CreateForCurrentUser()
    {
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("CURRENT_USER_SID_UNAVAILABLE");
        var security = new PipeSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            user,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return security;
    }
}
