using System;
using System.Windows.Forms;

namespace HKCLTool;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "PhysicsTool startup error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}

