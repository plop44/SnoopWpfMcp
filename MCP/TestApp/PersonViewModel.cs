using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace TestApp
{
    public class PersonViewModel : INotifyPropertyChanged, IDataErrorInfo
    {
        private string _firstName = "";
        private string _lastName = "";
        private int _age = 0;
        private string _email = "";
        private ObservableCollection<Person> _people;
        private string _statusMessage = "Ready";

        public PersonViewModel()
        {
            People = new ObservableCollection<Person>
            {
                new Person { FirstName = "John", LastName = "Doe", Age = 30, Email = "john.doe@email.com" },
                new Person { FirstName = "Jane", LastName = "Smith", Age = 25, Email = "jane.smith@email.com" },
                new Person { FirstName = "", LastName = "Invalid", Age = -5, Email = "invalid-email" }
            };

            // Initialize commands
            ClearDataCommand = new RelayCommand(ClearData);
            AddSampleDataCommand = new RelayCommand(AddSampleData);
            ShowInfoCommand = new RelayCommand(ShowInfo);
            ResetFormCommand = new RelayCommand(ResetForm);
        }

        public ObservableCollection<Person> People
        {
            get => _people;
            set
            {
                _people = value;
                OnPropertyChanged(nameof(People));
            }
        }

        public string FirstName
        {
            get => _firstName;
            set
            {
                _firstName = value;
                OnPropertyChanged(nameof(FirstName));
            }
        }

        public string LastName
        {
            get => _lastName;
            set
            {
                _lastName = value;
                OnPropertyChanged(nameof(LastName));
            }
        }

        public int Age
        {
            get => _age;
            set
            {
                _age = value;
                OnPropertyChanged(nameof(Age));
            }
        }

        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                OnPropertyChanged(nameof(Email));
            }
        }

        public string Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(FirstName):
                        return string.IsNullOrWhiteSpace(FirstName) ? "First name is required" : string.Empty;
                    case nameof(Age):
                        return Age < 0 ? "Age cannot be negative" : string.Empty;
                    case nameof(Email):
                        return string.IsNullOrWhiteSpace(Email) || !Email.Contains("@") ? "Valid email is required" : string.Empty;
                    case nameof(LastName):
                        return string.Empty;
                    default:
                        return string.Empty;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        // Commands
        public ICommand ClearDataCommand { get; private set; }
        public ICommand AddSampleDataCommand { get; private set; }
        public ICommand ShowInfoCommand { get; private set; }
        public ICommand ResetFormCommand { get; private set; }

        // Command methods
        private void ClearData()
        {
            People.Clear();
            StatusMessage = $"Data cleared at {DateTime.Now:HH:mm:ss}";
        }

        private void AddSampleData()
        {
            People.Add(new Person { FirstName = "Alice", LastName = "Johnson", Age = 28, Email = "alice.johnson@email.com" });
            StatusMessage = $"Sample data added at {DateTime.Now:HH:mm:ss}";
        }

        private void ShowInfo()
        {
            StatusMessage = $"Total people: {People.Count} | Current time: {DateTime.Now:HH:mm:ss}";
        }

        private void ResetForm()
        {
            FirstName = "";
            LastName = "";
            Age = 0;
            Email = "";
            StatusMessage = $"Form reset at {DateTime.Now:HH:mm:ss}";
        }
    }

    public class Person : INotifyPropertyChanged, IDataErrorInfo
    {
        private string _firstName;
        private string _lastName;
        private int _age;
        private string _email;

        public string FirstName
        {
            get => _firstName;
            set
            {
                _firstName = value;
                OnPropertyChanged(nameof(FirstName));
            }
        }

        public string LastName
        {
            get => _lastName;
            set
            {
                _lastName = value;
                OnPropertyChanged(nameof(LastName));
            }
        }

        public int Age
        {
            get => _age;
            set
            {
                _age = value;
                OnPropertyChanged(nameof(Age));
            }
        }

        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                OnPropertyChanged(nameof(Email));
            }
        }

        public string Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(FirstName):
                        return string.IsNullOrWhiteSpace(FirstName) ? "First name is required" : string.Empty;
                    case nameof(Age):
                        return Age < 0 ? "Age cannot be negative" : string.Empty;
                    case nameof(Email):
                        return string.IsNullOrWhiteSpace(Email) || !Email.Contains("@") ? "Valid email is required" : string.Empty;
                    case nameof(LastName):
                        return string.Empty;
                    default:
                        return string.Empty;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}