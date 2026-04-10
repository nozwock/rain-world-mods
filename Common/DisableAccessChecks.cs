using System.Security.Permissions;

// Allows access to private members on runtime
#pragma warning disable CS0618 // It's enforced by the mono runtime game's using
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618
