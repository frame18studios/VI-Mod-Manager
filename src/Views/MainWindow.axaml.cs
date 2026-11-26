using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViModManager.Core;
using ViModManager.Models;

namespace ViModManager.Views;

public partial class MainWindow : Window
{
    private static readonly (string Title, string Subtitle, string Icon)[] Sections =
    {
        ("Overview", "Library health, quick actions and recent changes.", Icons.Overview),
        ("Library", "Import, configure and manage your packages.", Icons.Library),
        ("Load order", "Control deployment priority and resolve overlaps.", Icons.Order),
        ("Profiles", "Save and switch complete workspace snapshots.", Icons.Profiles),
        ("Target folder", "Select where packages are deployed.", Icons.Target),
        ("History", "Chronological log of all workspace actions.", Icons.History),
        ("Customization", "Adjust colors, corners and text size.", Icons.Customization),
    };

    private readonly AppServices _services;
    private readonly Dictionary<int, Control> _pages = new();
    private readonly Dictionary<int, Func<Control>> _pageBuilders = new();
    private readonly List<ToggleButton> _navButtons = new();
    private readonly List<(ConfigField Field, Func<string> Read)> _fieldReaders = new();
    private readonly List<(Border Row, TextBox Key, TextBox Value)> _customRows = new();
    private readonly Dictionary<string, (Button Button, Border Swatch)> _colorRows = new();
    private readonly Dictionary<string, Border> _presetCards = new();
    private readonly Dictionary<string, Button> _cornerButtons = new();
    private readonly Dictionary<Control, double> _baseFontSizes = new();

    private ContentControl _pageHost = null!;
    private TextBlock _pageTitle = null!;
    private TextBlock _pageSub = null!;
    private TextBlock _pageKicker = null!;
    private TextBlock _targetChip = null!;
    private Border _targetChipBorder = null!;
    private TextBlock _targetFolders = null!;
    private TextBlock _sideTargetPath = null!;
    private TextBlock _sideProfile = null!;
    private Border _statusDot = null!;

    private StackPanel _modCards = null!;
    private StackPanel _detailPanel = null!;
    private StackPanel _customRowsPanel = null!;
    private TextBox _search = null!;
    private ComboBox _typeFilter = null!;
    private ComboBox _statusFilter = null!;
    private TextBlock _libraryCount = null!;
    private string _kindFilter = "All";
    private string _statusFilterValue = "All";

    private StackPanel _orderPanel = null!;
    private StackPanel _conflictPanel = null!;
    private StackPanel _profilesPanel = null!;
    private TextBox _profileName = null!;

    private StackPanel _activityPanel = null!;
    private StackPanel _healthContainer = null!;
    private TextBlock _statLibrary = null!;
    private TextBlock _statEnabled = null!;
    private TextBlock _statLoaded = null!;
    private TextBlock _statVariables = null!;

    private TextBox _historyBox = null!;
    private TextBox _historySearch = null!;
    private TextBox _targetPath = null!;
    private TextBlock _targetInfo = null!;
    private Slider _fontSizeSlider = null!;
    private TextBlock _fontSizeLabel = null!;
    private TextBlock _dataRootText = null!;
    private TextBlock _colorTokenName = null!;
    private TextBlock _colorTokenHelp = null!;
    private Border _colorPreview = null!;
    private ColorWheelControl _colorWheel = null!;
    private Slider _colorBrightness = null!;

    private string? _selectedModId;
    private string _selectedColorKey = "accent";
    private bool _suppressFontEvent;
    private bool _suppressColorEvent;
    private int _pageIndex;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        RootGrid.Margin = new Thickness(16);
        BuildUi();
        SetupDragDrop();
        RefreshSidebarProfile();
        Opened += (_, _) => ApplyFontScale();
        Go(0);
    }

    private void SetupDragDrop()
    {
        AddHandler(DragDrop.DragOverEvent, (_, args) =>
        {
            args.DragEffects = args.DataTransfer.Contains(DataFormat.File)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        });

        AddHandler(DragDrop.DropEvent, async (_, args) =>
        {
            var items = args.DataTransfer.TryGetFiles();
            if (items is null || items.Length == 0)
            {
                return;
            }

            var paths = items.Select(item => item.TryGetLocalPath()).OfType<string>().ToList();
            if (paths.Count > 0)
            {
                await ImportPathsAsync(paths);
            }
        });
    }

    // ------------------------------------------------------------------- shell
    private void BuildUi()
    {
        RootGrid.ColumnDefinitions = new ColumnDefinitions("252,*");

        Border sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        RootGrid.Children.Add(sidebar);

        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Control header = BuildHeader();
        Grid.SetRow(header, 0);
        content.Children.Add(header);

        _pageHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetRow(_pageHost, 1);
        content.Children.Add(_pageHost);
        content.Margin = new Thickness(20, 0, 0, 0);
        Grid.SetColumn(content, 1);
        RootGrid.Children.Add(content);

        _pageBuilders[0] = BuildOverviewPage;
        _pageBuilders[1] = BuildLibraryPage;
        _pageBuilders[2] = BuildOrderPage;
        _pageBuilders[3] = BuildProfilesPage;
        _pageBuilders[4] = BuildTargetPage;
        _pageBuilders[5] = BuildHistoryPage;
        _pageBuilders[6] = BuildSettingsPage;
    }

    private Border BuildSidebar()
    {
        var brandText = Ui.VStack(3);
        brandText.Children.Add(Ui.Text("VI Mod Manager", null, false, 16.5, FontWeight.Bold));
        var versionBadge = Ui.Badge("v0.1 · experimental", "accent");
        versionBadge.Margin = new Thickness(0, 2, 0, 0);
        versionBadge.HorizontalAlignment = HorizontalAlignment.Left;
        brandText.Children.Add(versionBadge);
        brandText.VerticalAlignment = VerticalAlignment.Center;

        var brand = Ui.HStack(12);
        brand.Children.Add(Icons.LogoMark(46));
        brand.Children.Add(brandText);

        var top = Ui.VStack(6);
        top.Children.Add(brand);
        top.Children.Add(Ui.Rule(14, 10));
        var navLabel = Ui.Overline("WORKSPACE");
        navLabel.Margin = new Thickness(6, 0, 0, 4);
        top.Children.Add(navLabel);

        for (int position = 0; position < Sections.Length; position++)
        {
            int target = position;
            var itemRow = Ui.HStack(11);
            itemRow.VerticalAlignment = VerticalAlignment.Center;
            itemRow.Children.Add(Icons.Vector(Sections[position].Icon, 15));
            itemRow.Children.Add(new TextBlock
            {
                Text = Sections[position].Title,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var button = new ToggleButton
            {
                Content = itemRow,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            button.Classes.Add("nav");
            button.Click += (_, _) => Go(target);
            top.Children.Add(button);
            _navButtons.Add(button);
        }

        var statusBody = Ui.VStack(9);
        statusBody.Children.Add(Ui.Kicker("DEPLOYMENT TARGET"));
        var dotRow = Ui.HStack(8);
        _statusDot = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(5),
            Background = Ui.Brush("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        dotRow.Children.Add(_statusDot);
        dotRow.Children.Add(Ui.Muted("Active folder", false, 11.5));
        statusBody.Children.Add(dotRow);
        _sideTargetPath = Ui.Mono("Not set", true);
        statusBody.Children.Add(_sideTargetPath);
        statusBody.Children.Add(Ui.Rule(2, 2));
        _sideProfile = Ui.Muted("No profile applied", true, 11.5);
        statusBody.Children.Add(_sideProfile);
        statusBody.Children.Add(Ui.Btn("Configure target →", () => Go(4), "link"));

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        grid.Children.Add(top);
        var status = Ui.Inset(statusBody, new Thickness(15));
        Grid.SetRow(status, 2);
        grid.Children.Add(status);

        return Ui.Panel(grid, "sidebar", new Thickness(16));
    }

    private Control BuildHeader()
    {
        var titles = Ui.VStack(4);
        _pageKicker = Ui.Kicker("WORKSPACE");
        _pageTitle = Ui.Text(Sections[0].Title, "h1", true);
        _pageSub = Ui.Muted(Sections[0].Subtitle, true, 12.5);
        titles.Children.Add(_pageKicker);
        titles.Children.Add(_pageTitle);
        titles.Children.Add(_pageSub);

        _targetChip = new TextBlock
        {
            Text = "NO TARGET",
            Foreground = Ui.Brush("MutedBrush"),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"),
            FontSize = 11,
            LineHeight = 15,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _targetChipBorder = new Border
        {
            Child = _targetChip,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 7),
            BorderThickness = new Thickness(1),
            Background = Ui.Brush("PanelBrush"),
            BorderBrush = Ui.Brush("LineStrongBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        _targetChipBorder.PointerPressed += (_, _) => Go(4);

        Button deploy = Ui.BusyBtn("Deploy all", LoadAllAsync, "soft", "Deploy every enabled package in load order.");
        Button import = Ui.BusyBtn("Import", ImportAsync, "primary", "Import ZIP package, folder or single file.");

        var actions = Ui.HStack(10);
        actions.VerticalAlignment = VerticalAlignment.Center;
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        actions.Children.Add(_targetChipBorder);
        actions.Children.Add(deploy);
        actions.Children.Add(import);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
        };
        grid.Children.Add(titles);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        var rule = Ui.Rule(12, 14);
        Grid.SetRow(rule, 1);
        Grid.SetColumnSpan(rule, 2);
        grid.Children.Add(rule);
        return grid;
    }

    private void Go(int index)
    {
        if (!_pages.TryGetValue(index, out Control? page))
        {
            if (!_pageBuilders.TryGetValue(index, out Func<Control>? buildPage))
            {
                return;
            }

            page = buildPage();
            _pages[index] = page;
        }

        _pageIndex = index;
        _pageHost.Content = page;
        for (int position = 0; position < _navButtons.Count; position++)
        {
            _navButtons[position].IsChecked = position == index;
        }

        _pageKicker.Text = "VI MOD MANAGER";
        _pageTitle.Text = Sections[index].Title;
        _pageSub.Text = Sections[index].Subtitle;

        RefreshTarget();

        switch (index)
        {
            case 0:
                RefreshOverview();
                break;
            case 1:
                RefreshLibrary();
                break;
            case 2:
                RefreshOrder();
                break;
            case 3:
                RefreshProfiles();
                break;
            case 5:
                RefreshHistory();
                break;
            case 6:
                RefreshSettings();
                break;
        }

        if (IsLoaded)
        {
            Dispatcher.UIThread.Post(ApplyFontScale, DispatcherPriority.Render);
        }
    }

    // ----------------------------------------------------------------- refresh
    private void RefreshAll()
    {
        if (_pages.ContainsKey(0)) RefreshOverview();
        if (_pages.ContainsKey(1)) RefreshLibrary();
        if (_pages.ContainsKey(2)) RefreshOrder();
        if (_pages.ContainsKey(3)) RefreshProfiles();
        if (_pages.ContainsKey(5)) RefreshHistory();
        if (_pages.ContainsKey(6)) RefreshSettings();
        RefreshTarget();
        RefreshSidebarProfile();
        Dispatcher.UIThread.Post(ApplyFontScale, DispatcherPriority.Render);
    }

    private void SelectMod(string modId)
    {
        _selectedModId = modId;
        if (_pageIndex == 1)
        {
            RefreshLibrary();
        }
        else
        {
            Go(1);
        }
    }

    private static T Spaced<T>(T control, double right = 10, double bottom = 6) where T : Control
    {
        control.Margin = new Thickness(0, 0, right, bottom);
        return control;
    }

    private static (Border Card, StackPanel Body) SectionCard(string title, string? subtitle = null)
    {
        var body = Ui.VStack(12);
        body.Children.Add(Ui.SectionHeader(title, subtitle));
        var card = Ui.Panel(body, "panel");
        return (card, body);
    }

    private static ScrollViewer PageScroller(Control content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(2, 2, 12, 24),
    };

    private void ApplyFontScale()
    {
        double scale = Math.Clamp(_services.Settings.Current.FontSize, 10, 16) / 13d;
        var controls = new List<Control>();
        var pending = new Stack<Visual>();
        pending.Push(RootGrid);

        while (pending.Count > 0)
        {
            Visual visual = pending.Pop();
            if (visual is Control control)
            {
                controls.Add(control);
            }

            foreach (Visual child in visual.GetVisualChildren())
            {
                pending.Push(child);
            }
        }

        foreach (Control control in controls)
        {
            if (TryGetFontSize(control, out double size) && !_baseFontSizes.ContainsKey(control))
            {
                _baseFontSizes.Add(control, size);
            }
        }

        foreach (Control control in controls)
        {
            if (_baseFontSizes.TryGetValue(control, out double size))
            {
                SetFontSize(control, size * scale);
            }
        }
    }

    private static bool TryGetFontSize(Control control, out double size)
    {
        switch (control)
        {
            case TextBlock textBlock:
                size = textBlock.FontSize;
                return true;
            case TemplatedControl templatedControl:
                size = templatedControl.FontSize;
                return true;
            default:
                size = 0;
                return false;
        }
    }

    private static void SetFontSize(Control control, double size)
    {
        switch (control)
        {
            case TextBlock textBlock:
                textBlock.FontSize = size;
                break;
            case TemplatedControl templatedControl:
                templatedControl.FontSize = size;
                break;
        }
    }
}
