using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace AllPurposeAssistant.Views;

public partial class CalendarControl : UserControl
{
    private static readonly ChineseLunisolarCalendar Lunar = new ChineseLunisolarCalendar();
    private static readonly string[] MonthNames =
        { "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月" };
    private static readonly string[] DayNames =
    {
        "初一","初二","初三","初四","初五","初六","初七","初八","初九","初十",
        "十一","十二","十三","十四","十五","十六","十七","十八","十九","二十",
        "廿一","廿二","廿三","廿四","廿五","廿六","廿七","廿八","廿九","三十"
    };

    private static readonly Brush Accent = MakeBrush("#4A90D9");
    private static readonly Brush TextMain = MakeBrush("#2C3E50");
    private static readonly Brush TextDim = MakeBrush("#BDC3C7");
    private static readonly Brush TextWeekend = MakeBrush("#E74C3C");
    private static readonly Brush TextRed = MakeBrush("#C0392B");
    private static readonly Brush TextLunar = MakeBrush("#B0BEC5");

    private DateTime _currentMonth;
    private Button[] _dayButtons = new Button[0];
    private readonly DispatcherTimer _todayRefreshTimer = new();

    private static Brush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush();
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            brush.Color = c;
        }
        catch
        {
            brush.Color = Colors.DarkSlateGray;
        }
        return brush;
    }

    public CalendarControl()
    {
        InitializeComponent();
        _currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _todayRefreshTimer.Tick += TodayRefreshTimer_Tick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CollectDayButtons();
        RefreshCalendar();
        ScheduleTodayRefresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _todayRefreshTimer.Stop();
    }

    private void TodayRefreshTimer_Tick(object? sender, EventArgs e)
    {
        RefreshCalendar();
        _todayRefreshTimer.Interval = TimeSpan.FromDays(1);
    }

    private void ScheduleTodayRefresh()
    {
        var nextDay = DateTime.Today.AddDays(1).AddSeconds(1);
        _todayRefreshTimer.Interval = nextDay - DateTime.Now;
        _todayRefreshTimer.Start();
    }

    private void CollectDayButtons()
    {
        var grid = DaysGrid;
        if (grid == null || grid.Children.Count < 42)
        {
            _dayButtons = new Button[0];
            return;
        }
        _dayButtons = new Button[42];
        for (int i = 0; i < 42; i++)
        {
            var button = (Button)grid.Children[i];
            button.IsHitTestVisible = false;
            button.Focusable = false;
            _dayButtons[i] = button;
        }
    }



    private static string GetLunarDay(DateTime solar)
    {
        int y = Lunar.GetYear(solar);
        int m = Lunar.GetMonth(solar);
        int d = Lunar.GetDayOfMonth(solar);
        bool isLeap = Lunar.IsLeapMonth(y, m);
        int realM = isLeap ? m - 1 : m;
        string monthName = (isLeap ? "闰" : "") + MonthNames[realM - 1];
        string dayName = DayNames[d - 1];
        return dayName == "初一" ? monthName : dayName;
    }

    private void RefreshCalendar()
    {
        if (_dayButtons.Length != 42) return;
        MonthYearLabel.Text = string.Format("{0}年{1}月", _currentMonth.Year, _currentMonth.Month);

        int startOfWeek = (int)_currentMonth.DayOfWeek;
        startOfWeek = startOfWeek == 0 ? 7 : startOfWeek;
        int daysInMonth = DateTime.DaysInMonth(_currentMonth.Year, _currentMonth.Month);
        var prevMonth = _currentMonth.AddMonths(-1);
        int daysInPrev = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
        var today = DateTime.Today;

        for (int i = 0; i < 42; i++)
        {
            var btn = _dayButtons[i];
            int boxIndex = i + 1;
            int dayNum;
            bool isCurrentMonth;

            if (boxIndex < startOfWeek)
            {
                dayNum = daysInPrev - (startOfWeek - boxIndex - 1);
                isCurrentMonth = false;
            }
            else if (boxIndex - startOfWeek + 1 > daysInMonth)
            {
                dayNum = boxIndex - startOfWeek - daysInMonth + 1;
                isCurrentMonth = false;
            }
            else
            {
                dayNum = boxIndex - startOfWeek + 1;
                isCurrentMonth = true;
            }

            var date = _currentMonth.AddDays(i - (startOfWeek - 1));
            string lunarText = GetLunarDay(date);
            bool isToday = isCurrentMonth && date.Year == today.Year && date.Month == today.Month && date.Day == today.Day;
            bool isWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;

            btn.Content = BuildCell(dayNum, lunarText, isToday, isWeekend, isCurrentMonth);
            btn.IsEnabled = isCurrentMonth;
            btn.Visibility = Visibility.Visible;
            btn.Background = isToday ? Accent : Brushes.Transparent;
        }
    }

    private static FrameworkElement BuildCell(int dayNum, string lunar, bool isToday, bool isWeekend, bool isCurrentMonth)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var solar = new TextBlock
        {
            Text = dayNum.ToString(),
            FontSize = 13,
            FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
            FontFamily = new FontFamily("Microsoft YaHei"),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var luna = new TextBlock
        {
            Text = lunar,
            FontSize = 9,
            FontFamily = new FontFamily("Microsoft YaHei"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0)
        };

        if (isToday)
        {
            solar.Foreground = Brushes.White;
            luna.Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
        }
        else if (!isCurrentMonth)
        {
            solar.Foreground = TextDim;
            luna.Foreground = TextDim;
        }
        else if (isWeekend)
        {
            solar.Foreground = TextWeekend;
            luna.Foreground = TextRed;
        }
        else
        {
            solar.Foreground = TextMain;
            luna.Foreground = lunar == "初一" ? TextRed : TextLunar;
        }

        Grid.SetRow(solar, 0);
        Grid.SetRow(luna, 1);
        grid.Children.Add(solar);
        grid.Children.Add(luna);
        return grid;
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _currentMonth = _currentMonth.AddMonths(-1);
        RefreshCalendar();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _currentMonth = _currentMonth.AddMonths(1);
        RefreshCalendar();
    }

    private void Day_Click(object sender, RoutedEventArgs e)
    {
    }
}
