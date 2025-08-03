using System;
using System.Diagnostics;
using System.Windows;

namespace TestApp
{
    public partial class MainWindow : Window
    {
        private int clickCounter = 0;
        private PersonViewModel personViewModel;

        public MainWindow()
        {
            InitializeComponent();
            personViewModel = new PersonViewModel();
            DataContext = personViewModel;
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

        private void AddPersonButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(personViewModel.FirstName) && 
                personViewModel.Age >= 0 && 
                !string.IsNullOrWhiteSpace(personViewModel.Email) && 
                personViewModel.Email.Contains("@"))
            {
                personViewModel.People.Add(new Person
                {
                    FirstName = personViewModel.FirstName,
                    LastName = personViewModel.LastName,
                    Age = personViewModel.Age,
                    Email = personViewModel.Email
                });

                personViewModel.FirstName = "";
                personViewModel.LastName = "";
                personViewModel.Age = 0;
                personViewModel.Email = "";
            }
        }
    }
}
