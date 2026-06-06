using System.Windows;
using System.Windows.Controls;
using OKXMonitor.Services;

namespace OKXMonitor.Views;

public partial class SettingsWindow : Window
{
    readonly Store _store;
    readonly AppSettings _settings;

    public SettingsWindow(Store store, AppSettings settings)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;

        var c = CredentialStore.Load();
        ApiKeyBox.Text = c.ApiKey;
        SecretBox.Text = c.SecretKey;
        PassphraseBox.Text = c.Passphrase;
        DemoCheck.IsChecked = c.Demo;
        HostBox.Text = string.IsNullOrWhiteSpace(_settings.Host) ? OkxClient.DefaultHost : _settings.Host;
        IntervalSlider.Value = _settings.RefreshInterval;
    }

    void HostPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (HostPreset.SelectedItem is ComboBoxItem item && item.Content is string h)
            HostBox.Text = h;
    }

    void Interval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IntervalLabel != null) IntervalLabel.Text = $"{(int)e.NewValue}s";
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Text.Trim();
        var secret = SecretBox.Text.Trim();
        var passphrase = PassphraseBox.Text; // do NOT trim interior; passphrases may contain spaces
        if (apiKey.Length == 0 || secret.Length == 0 || passphrase.Length == 0)
        {
            MessageBox.Show("API Key / Secret / Passphrase 不能为空。", "OKX");
            return;
        }

        _settings.Host = HostBox.Text.Trim();
        _settings.RefreshInterval = IntervalSlider.Value;
        _settings.Save();
        _store.UpdateSettings(_settings);
        _store.UpdateCredentials(new Credentials(apiKey, secret, passphrase, DemoCheck.IsChecked == true));
        Close();
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("确定删除已保存的凭据？", "OKX", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            CredentialStore.Delete();
            _store.UpdateCredentials(Credentials.Empty);
            Close();
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
