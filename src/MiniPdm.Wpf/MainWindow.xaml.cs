using System.Windows;

namespace MiniPdm.Wpf;

// Окно. Здесь только два вызова: разметка и подстановка модели представления,
// которую собрал App. Вся остальная логика — в командах моделей представления.
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DataContext = ((App)Application.Current).ViewModel;
    }
}
