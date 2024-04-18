using System.Windows;
using System.Windows.Media.Imaging;


namespace CodeGen;
public partial class FileNameDialog : Window
{
    private const string DEFAULT_TEXT = "Select a entity name";

    public FileNameDialog(string folder, string[] entities)
    {
        InitializeComponent();
        lblFolder.Content = string.Format("{0}/", folder);
        foreach (var item in entities)
        {
            selectName.Items.Add(item);
        }
        selectName.Text = DEFAULT_TEXT;
        selectName.SelectionChanged += (s, e) =>
        {
            btnCreate.IsEnabled = true;
        };
        Loaded += (s, e) =>
    {
        BitmapImage icon = new();
        icon.BeginInit();
        icon.UriSource = new Uri("pack://application:,,,/CodeGen;component/Resources/AddApplicationInsights.png");
        icon.EndInit();
        Title = "Code Generator";
    };
    }

    public string Input => selectName.SelectedItem?.ToString();


    private void Button_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
