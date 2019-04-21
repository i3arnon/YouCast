using System.Security.Principal;

namespace YouCast.Helpers
{
    public class PermissionsHelper
    {
        public static bool IsRunAsAdministrator()
        {
            var windowsPrincipal = new WindowsPrincipal(WindowsIdentity.GetCurrent());

            return windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}