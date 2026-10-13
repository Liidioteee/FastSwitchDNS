using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace FastSwitchDNS
{
    /// <summary>
    /// Autostart via Task Scheduler: the app needs admin rights, and elevated apps in the
    /// registry Run key are silently skipped at logon. A task with "highest privileges" is not.
    /// </summary>
    static class Autostart
    {
        const string TaskName = "FastSwitchDNS";

        public static bool IsEnabled() =>
            DnsManager.Run("schtasks.exe", $"/query /tn \"{TaskName}\"").code == 0;

        public static void Set(bool enable)
        {
            if (!enable)
            {
                DnsManager.Run("schtasks.exe", $"/delete /tn \"{TaskName}\" /f");
                return;
            }

            var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            var exe = SecurityElement.Escape(Application.ExecutablePath);
            // XML instead of plain /create flags so the task also starts on battery power
            var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{user}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec><Command>{exe}</Command></Exec>
  </Actions>
</Task>";

            var file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, xml, Encoding.Unicode);
                var (code, output) = DnsManager.Run("schtasks.exe", $"/create /tn \"{TaskName}\" /xml \"{file}\" /f");
                if (code != 0) throw new System.Exception(output.Trim());
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}
