using System;
using System.Diagnostics;
using System.Windows;

namespace TestApp
{
    public partial class MainWindow : Window
    {
        private int clickCounter = 0;

        public MainWindow()
        {
            InitializeComponent();
            UpdateProcessInfo();
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            clickCounter++;
            UpdateProcessInfo();
            UpdateClickCounter();
        }

        private void UpdateProcessInfo()
        {
            var process = Process.GetCurrentProcess();
            ProcessInfoText.Text = $"Process ID: {process.Id} | Process Name: {process.ProcessName}";
        }

        private void UpdateClickCounter()
        {
            ClickCounterText.Text = $"Button clicks: {clickCounter}";
        }
    }
}
