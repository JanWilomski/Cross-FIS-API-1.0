using System;
using System.Windows;

namespace Cross_FIS_API_1._0
{
    /// <summary>
    /// Main application entry point
    /// </summary>
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                var application = new Application();
                var mainWindow = new MainWindow();
                
                application.Run(mainWindow);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Application startup error: {ex.Message}", "Startup Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}