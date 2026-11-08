using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace SetupSwitch.App;

/// <summary>Pergunta se a troca deu certo e reverte sozinha se ninguém responder.</summary>
public partial class ConfirmWindow : Window
{
    private const int Seconds = 15;

    private readonly TaskCompletionSource<bool> _answer = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _remaining = Seconds;

    public ConfirmWindow(string label)
    {
        InitializeComponent();

        TitleText.Text = $"{label}: manter esta configuração?";
        KeepButton.Style = TryFindResource("AccentButtonStyle") as Style ?? KeepButton.Style;
        UpdateCountdown();

        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => CenterOnPrimaryScreen();
    }

    public Task<bool> WaitForAnswer()
    {
        Show();
        Activate();
        _timer.Start();
        return _answer.Task;
    }

    public void Revert() => Finish(keep: false);

    private void Tick()
    {
        _remaining--;
        UpdateCountdown();

        if (_remaining <= 0)
        {
            Revert();
        }
    }

    private void UpdateCountdown() =>
        CountdownText.Text = $"Sem resposta, a configuração anterior volta em {_remaining} s.";

    private void Finish(bool keep)
    {
        _timer.Stop();
        if (_answer.TrySetResult(keep))
        {
            Close();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // Fechar pelo X conta como não confirmar.
        if (_answer.TrySetResult(false))
        {
            _timer.Stop();
        }
    }

    private void CenterOnPrimaryScreen()
    {
        Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        Top = (SystemParameters.PrimaryScreenHeight - ActualHeight) / 2;
    }

    private void OnKeep(object sender, RoutedEventArgs e) => Finish(keep: true);

    private void OnRevert(object sender, RoutedEventArgs e) => Revert();
}
