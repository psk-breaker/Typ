using System.Windows;

namespace Writing_App
{
    public partial class RenameDialog : Window
    {
        public string ResponseText { get; private set; }

        public RenameDialog(string prompt, string initialValue)
        {
            InitializeComponent();
            PromptText.Text = prompt;
            InputBox.Text = initialValue;
            InputBox.SelectAll();
            InputBox.Focus();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ResponseText = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(ResponseText))
            {
                MessageBox.Show("Name cannot be empty.", "Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }
    }
}
