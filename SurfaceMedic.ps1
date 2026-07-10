<#
    SurfaceMedic v0.2.0
    Tune-up and thermal toolkit for heavily-used Surface (and other Windows) devices.

    Features:
      - Dashboard: device, OS, battery wear, storage, disk health, thermal snapshot
      - Thermal Events: scans the System event log for throttle/thermal/WHEA events
      - Power: Turbo Boost cap (PROCTHROTTLEMAX), power-mode overlays, battery report
      - Software: winget search/install/upgrade + one-click diagnostic tools
      - Maintenance: temp cleanup, SFC, DISM, component store, firmware update links

    Usage:
      powershell -ExecutionPolicy Bypass -File SurfaceMedic.ps1
      (auto-elevates; requires Windows 10/11, PowerShell 5.1+)

    -Smoke is a test-only flag: renders the UI offscreen without activation,
    captures screenshots to .\screenshots\, and exits. Never shows a window.
#>
[CmdletBinding()]
param(
    [switch]$Smoke
)

$ErrorActionPreference = 'Stop'
$script:AppName    = 'SurfaceMedic'
$script:AppVersion = '0.2.0'

# ------------------------------------------------------------------ elevation
$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$script:IsAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $script:IsAdmin -and -not $Smoke) {
    $exe = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' }
    Start-Process $exe -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    exit
}

# ------------------------------------------------------------------ assemblies
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

if (-not ([System.Management.Automation.PSTypeName]'SurfaceMedic.PkgRow').Type) {
    Add-Type -TypeDefinition @'
namespace SurfaceMedic {
    public class PkgRow {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Version { get; set; }
        public string Available { get; set; }
        public string Source { get; set; }
    }
    public class EvtRow {
        public System.DateTime SortTime { get; set; }
        public string Time { get; set; }
        public string Level { get; set; }
        public string Provider { get; set; }
        public string Id { get; set; }
        public string Message { get; set; }
    }
}
'@
}

if (-not ([System.Management.Automation.PSTypeName]'SurfaceMedic.Dwm').Type) {
    Add-Type -Namespace SurfaceMedic -Name Dwm -MemberDefinition @'
[DllImport("dwmapi.dll")]
public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
'@
}

# ------------------------------------------------------------------ XAML
$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="SurfaceMedic v0.2.0" Width="1240" Height="820" MinWidth="1020" MinHeight="660"
        WindowStartupLocation="CenterScreen" Background="#1E1E2E"
        FontFamily="Segoe UI" FontSize="13" Foreground="#CDD6F4">
  <Window.Resources>
    <SolidColorBrush x:Key="Base"   Color="#1E1E2E"/>
    <SolidColorBrush x:Key="Mantle" Color="#181825"/>
    <SolidColorBrush x:Key="Crust"  Color="#11111B"/>
    <SolidColorBrush x:Key="Surf0"  Color="#313244"/>
    <SolidColorBrush x:Key="Surf1"  Color="#45475A"/>
    <SolidColorBrush x:Key="TextBr" Color="#CDD6F4"/>
    <SolidColorBrush x:Key="Sub"    Color="#A6ADC8"/>
    <SolidColorBrush x:Key="Mauve"  Color="#CBA6F7"/>
    <SolidColorBrush x:Key="Blue"   Color="#89B4FA"/>
    <SolidColorBrush x:Key="Green"  Color="#A6E3A1"/>
    <SolidColorBrush x:Key="Red"    Color="#F38BA8"/>
    <SolidColorBrush x:Key="Yellow" Color="#F9E2AF"/>
    <SolidColorBrush x:Key="Peach"  Color="#FAB387"/>

    <Style x:Key="ScrollThumb" TargetType="Thumb">
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Thumb">
            <Border Background="#45475A" CornerRadius="4"/>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType="ScrollBar">
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="Width" Value="10"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="ScrollBar">
            <Grid Background="Transparent">
              <Track Name="PART_Track" IsDirectionReversed="True">
                <Track.Thumb>
                  <Thumb Style="{StaticResource ScrollThumb}"/>
                </Track.Thumb>
              </Track>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property="Orientation" Value="Horizontal">
          <Setter Property="Height" Value="10"/>
          <Setter Property="Width" Value="Auto"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ScrollBar">
                <Grid Background="Transparent">
                  <Track Name="PART_Track" IsDirectionReversed="False">
                    <Track.Thumb>
                      <Thumb Style="{StaticResource ScrollThumb}"/>
                    </Track.Thumb>
                  </Track>
                </Grid>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Trigger>
      </Style.Triggers>
    </Style>

    <Style TargetType="Button">
      <Setter Property="Background" Value="{StaticResource Surf0}"/>
      <Setter Property="Foreground" Value="{StaticResource TextBr}"/>
      <Setter Property="Padding" Value="14,7"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="bd" Background="{TemplateBinding Background}" CornerRadius="6" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="bd" Property="Opacity" Value="0.85"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="bd" Property="Opacity" Value="0.7"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.45"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="AccentBtn" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
      <Setter Property="Background" Value="{StaticResource Mauve}"/>
      <Setter Property="Foreground" Value="{StaticResource Crust}"/>
    </Style>
    <Style x:Key="SmallBtn" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
      <Setter Property="Padding" Value="10,4"/>
      <Setter Property="FontSize" Value="11"/>
    </Style>

    <Style TargetType="TextBox">
      <Setter Property="Background" Value="{StaticResource Crust}"/>
      <Setter Property="Foreground" Value="{StaticResource TextBr}"/>
      <Setter Property="BorderBrush" Value="{StaticResource Surf1}"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Padding" Value="8,6"/>
      <Setter Property="CaretBrush" Value="{StaticResource Mauve}"/>
      <Setter Property="SelectionBrush" Value="{StaticResource Surf1}"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="TextBox">
            <Border x:Name="bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6">
              <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsKeyboardFocused" Value="True">
                <Setter TargetName="bd" Property="BorderBrush" Value="{StaticResource Mauve}"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style TargetType="TabControl">
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Padding" Value="0"/>
    </Style>
    <Style TargetType="TabItem">
      <Setter Property="Foreground" Value="{StaticResource Sub}"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="TabItem">
            <Border x:Name="bd" Background="Transparent" Padding="16,9,16,4" Margin="0,0,4,0" CornerRadius="8,8,0,0">
              <StackPanel>
                <ContentPresenter ContentSource="Header" HorizontalAlignment="Center"/>
                <Rectangle x:Name="ind" Height="2" Margin="0,6,0,0" Fill="Transparent" RadiusX="1" RadiusY="1"/>
              </StackPanel>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsSelected" Value="True">
                <Setter Property="Foreground" Value="{StaticResource TextBr}"/>
                <Setter TargetName="ind" Property="Fill" Value="{StaticResource Mauve}"/>
                <Setter TargetName="bd" Property="Background" Value="{StaticResource Mantle}"/>
              </Trigger>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Foreground" Value="{StaticResource TextBr}"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key="Card" TargetType="Border">
      <Setter Property="Background" Value="#27273B"/>
      <Setter Property="CornerRadius" Value="10"/>
      <Setter Property="Padding" Value="16"/>
      <Setter Property="Margin" Value="0,0,12,12"/>
    </Style>
    <Style x:Key="CardTitle" TargetType="TextBlock">
      <Setter Property="FontSize" Value="11"/>
      <Setter Property="FontWeight" Value="Bold"/>
      <Setter Property="Foreground" Value="{StaticResource Mauve}"/>
      <Setter Property="Margin" Value="0,0,0,8"/>
    </Style>
    <Style x:Key="CardBody" TargetType="TextBlock">
      <Setter Property="Foreground" Value="{StaticResource TextBr}"/>
      <Setter Property="TextWrapping" Value="Wrap"/>
      <Setter Property="LineHeight" Value="20"/>
    </Style>
    <Style x:Key="CardNote" TargetType="TextBlock">
      <Setter Property="Foreground" Value="{StaticResource Sub}"/>
      <Setter Property="TextWrapping" Value="Wrap"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="LineHeight" Value="18"/>
    </Style>

    <Style TargetType="DataGridColumnHeader">
      <Setter Property="Background" Value="{StaticResource Mantle}"/>
      <Setter Property="Foreground" Value="{StaticResource Sub}"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="FontSize" Value="12"/>
      <Setter Property="Padding" Value="10,8"/>
      <Setter Property="BorderThickness" Value="0,0,0,1"/>
      <Setter Property="BorderBrush" Value="{StaticResource Surf0}"/>
    </Style>
    <Style TargetType="DataGridCell">
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Padding" Value="8,6"/>
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="DataGridCell">
            <Border Padding="{TemplateBinding Padding}" Background="{TemplateBinding Background}">
              <ContentPresenter VerticalAlignment="Center"/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
      <Style.Triggers>
        <Trigger Property="IsSelected" Value="True">
          <Setter Property="Background" Value="{StaticResource Surf1}"/>
        </Trigger>
      </Style.Triggers>
    </Style>
    <Style TargetType="DataGrid">
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="Foreground" Value="{StaticResource TextBr}"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="GridLinesVisibility" Value="None"/>
      <Setter Property="HeadersVisibility" Value="Column"/>
      <Setter Property="AutoGenerateColumns" Value="False"/>
      <Setter Property="IsReadOnly" Value="True"/>
      <Setter Property="RowBackground" Value="Transparent"/>
      <Setter Property="AlternatingRowBackground" Value="#232336"/>
      <Setter Property="RowHeaderWidth" Value="0"/>
      <Setter Property="SelectionUnit" Value="FullRow"/>
      <Setter Property="CanUserAddRows" Value="False"/>
    </Style>
  </Window.Resources>

  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="190" MinHeight="90"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>

    <!-- header -->
    <Border Grid.Row="0" Background="{StaticResource Mantle}" Padding="20,14">
      <DockPanel>
        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Center">
          <ProgressBar x:Name="BusyBar" Width="110" Height="4" Visibility="Collapsed"
                       Foreground="{StaticResource Mauve}" Background="{StaticResource Surf0}"
                       BorderThickness="0" Margin="0,0,16,0" VerticalAlignment="Center"/>
          <TextBlock x:Name="AdminBadge" Text="Administrator" Foreground="{StaticResource Green}"
                     FontSize="12" FontWeight="SemiBold" VerticalAlignment="Center"/>
        </StackPanel>
        <StackPanel>
          <StackPanel Orientation="Horizontal">
            <TextBlock Text="SurfaceMedic" FontSize="20" FontWeight="Bold" Foreground="{StaticResource TextBr}"/>
            <Border Background="{StaticResource Surf0}" CornerRadius="6" Padding="8,2" Margin="10,2,0,0" VerticalAlignment="Center">
              <TextBlock Text="v0.2.0" FontSize="11" Foreground="{StaticResource Sub}"/>
            </Border>
          </StackPanel>
          <TextBlock Text="Surface tune-up and thermal toolkit" FontSize="12" Foreground="{StaticResource Sub}" Margin="0,2,0,0"/>
        </StackPanel>
      </DockPanel>
    </Border>

    <!-- tabs -->
    <TabControl x:Name="MainTabs" Grid.Row="1" Margin="12,10,12,0">

      <TabItem Header="Dashboard">
        <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="16,16,4,4">
          <StackPanel>
            <DockPanel Margin="0,0,12,14">
              <Button x:Name="BtnDashRefresh" Content="Refresh" DockPanel.Dock="Right" Style="{StaticResource AccentBtn}"/>
              <TextBlock Text="System overview" FontSize="17" FontWeight="Bold" VerticalAlignment="Center"/>
            </DockPanel>
            <WrapPanel>
              <Border Style="{StaticResource Card}" Width="380">
                <StackPanel>
                  <TextBlock Style="{StaticResource CardTitle}" Text="DEVICE"/>
                  <TextBlock x:Name="CardDevice" Style="{StaticResource CardBody}" Text="Loading..."/>
                </StackPanel>
              </Border>
              <Border Style="{StaticResource Card}" Width="380">
                <StackPanel>
                  <TextBlock Style="{StaticResource CardTitle}" Text="OPERATING SYSTEM"/>
                  <TextBlock x:Name="CardOs" Style="{StaticResource CardBody}" Text="Loading..."/>
                </StackPanel>
              </Border>
              <Border Style="{StaticResource Card}" Width="380">
                <StackPanel>
                  <TextBlock Style="{StaticResource CardTitle}" Text="BATTERY HEALTH"/>
                  <TextBlock x:Name="CardBattery" Style="{StaticResource CardBody}" Text="Loading..."/>
                </StackPanel>
              </Border>
              <Border Style="{StaticResource Card}" Width="380">
                <StackPanel>
                  <TextBlock Style="{StaticResource CardTitle}" Text="STORAGE"/>
                  <TextBlock x:Name="CardStorage" Style="{StaticResource CardBody}" Text="Loading..."/>
                </StackPanel>
              </Border>
              <Border Style="{StaticResource Card}" Width="380">
                <StackPanel>
                  <TextBlock Style="{StaticResource CardTitle}" Text="DISK HEALTH"/>
                  <TextBlock x:Name="CardDisk" Style="{StaticResource CardBody}" Text="Loading..."/>
                </StackPanel>
              </Border>
              <Border Style="{StaticResource Card}" Width="380">
                <StackPanel>
                  <TextBlock Style="{StaticResource CardTitle}" Text="THERMAL (LAST 7 DAYS)"/>
                  <TextBlock x:Name="CardThermal" Style="{StaticResource CardBody}" Text="Loading..."/>
                </StackPanel>
              </Border>
            </WrapPanel>
          </StackPanel>
        </ScrollViewer>
      </TabItem>

      <TabItem Header="Thermal Events">
        <Grid Margin="16">
          <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
          </Grid.RowDefinitions>
          <DockPanel Grid.Row="0">
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
              <Button x:Name="BtnDays7"  Content="7d"  Margin="0,0,6,0"/>
              <Button x:Name="BtnDays30" Content="30d" Margin="0,0,6,0"/>
              <Button x:Name="BtnDays90" Content="90d" Margin="0,0,14,0"/>
              <Button x:Name="BtnScanEvents" Content="Scan event log" Style="{StaticResource AccentBtn}" Margin="0,0,8,0"/>
              <Button x:Name="BtnExportEvents" Content="Export CSV"/>
            </StackPanel>
            <TextBlock Text="Thermal and throttling events" FontSize="17" FontWeight="Bold" VerticalAlignment="Center"/>
          </DockPanel>
          <TextBlock x:Name="TxtEventSummary" Grid.Row="1" Foreground="{StaticResource Sub}" Margin="0,10,0,8"
                     TextWrapping="Wrap"
                     Text="Scans the System log for Kernel-Power 125 (thermal throttle engaged), Kernel-Processor-Power 37 (firmware speed cap), WHEA hardware errors, and any thermal-keyword warnings."/>
          <DataGrid x:Name="GridEvents" Grid.Row="2">
            <DataGrid.RowStyle>
              <Style TargetType="DataGridRow">
                <Setter Property="Background" Value="Transparent"/>
                <Style.Triggers>
                  <DataTrigger Binding="{Binding Level}" Value="Error">
                    <Setter Property="Foreground" Value="{StaticResource Red}"/>
                  </DataTrigger>
                  <DataTrigger Binding="{Binding Level}" Value="Critical">
                    <Setter Property="Foreground" Value="{StaticResource Red}"/>
                  </DataTrigger>
                  <DataTrigger Binding="{Binding Level}" Value="Warning">
                    <Setter Property="Foreground" Value="{StaticResource Yellow}"/>
                  </DataTrigger>
                </Style.Triggers>
              </Style>
            </DataGrid.RowStyle>
            <DataGrid.Columns>
              <DataGridTextColumn Header="Time" Binding="{Binding Time}" Width="140"/>
              <DataGridTextColumn Header="Level" Binding="{Binding Level}" Width="90"/>
              <DataGridTextColumn Header="Provider" Binding="{Binding Provider}" Width="200"/>
              <DataGridTextColumn Header="Id" Binding="{Binding Id}" Width="60"/>
              <DataGridTextColumn Header="Message" Binding="{Binding Message}" Width="*"/>
            </DataGrid.Columns>
          </DataGrid>
        </Grid>
      </TabItem>

      <TabItem Header="Power">
        <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="16,16,4,4">
          <WrapPanel>
            <Border Style="{StaticResource Card}" Width="555">
              <StackPanel>
                <TextBlock Style="{StaticResource CardTitle}" Text="TURBO BOOST / CPU CEILING"/>
                <TextBlock x:Name="TxtTurbo" Style="{StaticResource CardBody}" Text="Reading current processor cap..." Margin="0,0,0,10"/>
                <TextBlock Style="{StaticResource CardNote}" Margin="0,0,0,12"
                           Text="Capping the processor at 99 percent disables Turbo Boost. On a thermally-limited Surface this typically runs 10-15 degrees cooler for about 10 percent peak performance loss, and sustained performance often improves because the firmware stops hitting the thermal wall."/>
                <StackPanel Orientation="Horizontal">
                  <Button x:Name="BtnCap99" Content="Cap at 99% (run cooler)" Style="{StaticResource AccentBtn}" Margin="0,0,8,0"/>
                  <Button x:Name="BtnCap100" Content="Restore 100%"/>
                </StackPanel>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}" Width="555">
              <StackPanel>
                <TextBlock Style="{StaticResource CardTitle}" Text="WINDOWS POWER MODE"/>
                <TextBlock x:Name="TxtOverlay" Style="{StaticResource CardBody}" Text="Reading active power mode..." Margin="0,0,0,10"/>
                <TextBlock Style="{StaticResource CardNote}" Margin="0,0,0,12"
                           Text="This is the same slider as Settings &gt; System &gt; Power. Best efficiency is the right daily-driver setting for a throttling device."/>
                <StackPanel Orientation="Horizontal">
                  <Button x:Name="BtnOvlEff" Content="Best efficiency" Style="{StaticResource AccentBtn}" Margin="0,0,8,0"/>
                  <Button x:Name="BtnOvlBal" Content="Balanced" Margin="0,0,8,0"/>
                  <Button x:Name="BtnOvlPerf" Content="Best performance"/>
                </StackPanel>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}" Width="555">
              <StackPanel>
                <TextBlock Style="{StaticResource CardTitle}" Text="BATTERY REPORT"/>
                <TextBlock Style="{StaticResource CardNote}" Margin="0,0,0,12"
                           Text="Generates the full Windows battery report (design vs full-charge capacity, cycle count, usage history) on your Desktop and opens it. A worn or swollen battery is the number one cause of aggressive thermal throttling on heavily-used Surfaces."/>
                <Button x:Name="BtnBattReport" Content="Generate and open battery report" Style="{StaticResource AccentBtn}" HorizontalAlignment="Left"/>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}" Width="555">
              <StackPanel>
                <TextBlock Style="{StaticResource CardTitle}" Text="ACTIVE POWER PLAN"/>
                <TextBlock x:Name="TxtPlan" Style="{StaticResource CardBody}" Text="Reading active plan..." Margin="0,0,0,12"/>
                <Button x:Name="BtnRefreshPower" Content="Refresh power status" HorizontalAlignment="Left"/>
              </StackPanel>
            </Border>
          </WrapPanel>
        </ScrollViewer>
      </TabItem>

      <TabItem Header="Software">
        <Grid Margin="16">
          <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
          </Grid.RowDefinitions>
          <Border Grid.Row="0" Style="{StaticResource Card}" Margin="0,0,0,12">
            <StackPanel>
              <TextBlock Style="{StaticResource CardTitle}" Text="RECOMMENDED DIAGNOSTIC TOOLS"/>
              <TextBlock Style="{StaticResource CardNote}" Margin="0,0,0,10"
                         Text="One-click winget installs. HWiNFO64 shows live temps, package power, and throttle flags. CrystalDiskInfo reads SSD health and wear."/>
              <StackPanel Orientation="Horizontal">
                <Button x:Name="BtnToolHwinfo" Content="Install HWiNFO64" Margin="0,0,8,0"/>
                <Button x:Name="BtnToolCdi" Content="Install CrystalDiskInfo" Margin="0,0,8,0"/>
                <Button x:Name="BtnToolLhm" Content="Install LibreHardwareMonitor" Margin="0,0,8,0"/>
                <Button x:Name="BtnToolPt" Content="Install PowerToys"/>
              </StackPanel>
            </StackPanel>
          </Border>
          <DockPanel Grid.Row="1" Margin="0,0,0,8">
            <Button x:Name="BtnInstallSel" Content="Install selected" DockPanel.Dock="Right" Margin="8,0,0,0"/>
            <Button x:Name="BtnSearch" Content="Search winget" DockPanel.Dock="Right" Style="{StaticResource AccentBtn}" Margin="8,0,0,0"/>
            <TextBox x:Name="TxtSearch" Height="32"/>
          </DockPanel>
          <DataGrid x:Name="GridPackages" Grid.Row="2">
            <DataGrid.Columns>
              <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="*"/>
              <DataGridTextColumn Header="Id" Binding="{Binding Id}" Width="280"/>
              <DataGridTextColumn Header="Version" Binding="{Binding Version}" Width="140"/>
              <DataGridTextColumn Header="Source" Binding="{Binding Source}" Width="90"/>
            </DataGrid.Columns>
          </DataGrid>
          <DockPanel Grid.Row="3" Margin="0,12,0,8">
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
              <Button x:Name="BtnCheckUpd" Content="Check for updates" Style="{StaticResource AccentBtn}" Margin="0,0,8,0"/>
              <Button x:Name="BtnUpgSel" Content="Upgrade selected" Margin="0,0,8,0"/>
              <Button x:Name="BtnUpgAll" Content="Upgrade all"/>
            </StackPanel>
            <TextBlock Text="Available updates" FontSize="15" FontWeight="Bold" VerticalAlignment="Center"/>
          </DockPanel>
          <DataGrid x:Name="GridUpdates" Grid.Row="4">
            <DataGrid.Columns>
              <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="*"/>
              <DataGridTextColumn Header="Id" Binding="{Binding Id}" Width="260"/>
              <DataGridTextColumn Header="Installed" Binding="{Binding Version}" Width="130"/>
              <DataGridTextColumn Header="Available" Binding="{Binding Available}" Width="130"/>
              <DataGridTextColumn Header="Source" Binding="{Binding Source}" Width="90"/>
            </DataGrid.Columns>
          </DataGrid>
        </Grid>
      </TabItem>

      <TabItem Header="Maintenance">
        <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="16,16,4,4">
          <WrapPanel>
            <Border Style="{StaticResource Card}" Width="555">
              <StackPanel>
                <TextBlock Style="{StaticResource CardTitle}" Text="TEMP FILES"/>
                <TextBlock Style="{StaticResource CardNote}" Margin="0,0,0,12"
                           Text="Clears the user and Windows temp folders. Runs immediately and reports how much space was freed. Files in use are skipped automatically."/>
                <Button x:Name="BtnCleanTemp" Content="Clean temp files now" Style="{StaticResource AccentBtn}" HorizontalAlignment="Left"/>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}" Width="555">
              <StackPanel>
                <TextBlock Style="{StaticResource CardTitle}" Text="SYSTEM FILE INTEGRITY"/>
                <TextBlock Style="{StaticResource CardNote}" Margin="0,0,0,12"
                           Text="On a heavily-used install, run DISM ScanHealth first; if it reports corruption, run RestoreHealth, then SFC. Output streams to the console below."/>
                <WrapPanel>
                  <Button x:Name="BtnDismScan" Content="DISM ScanHealth" Margin="0,0,8,8"/>
                  <Button x:Name="BtnDismRestore" Content="DISM RestoreHealth" Margin="0,0,8,8"/>
                  <Button x:Name="BtnSfc" Content="SFC /scannow" Margin="0,0,8,8"/>
                </WrapPanel>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}" Width="555">
              <StackPanel>
                <TextBlock Style="{StaticResource CardTitle}" Text="COMPONENT STORE (WINSXS)"/>
                <TextBlock Style="{StaticResource CardNote}" Margin="0,0,0,12"
                           Text="Removes superseded update components. Frees several GB on installs that have been updated for years. Safe, but takes a while."/>
                <Button x:Name="BtnCompClean" Content="Run component cleanup" HorizontalAlignment="Left"/>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}" Width="555">
              <StackPanel>
                <TextBlock Style="{StaticResource CardTitle}" Text="UPDATES AND FIRMWARE"/>
                <TextBlock Style="{StaticResource CardNote}" Margin="0,0,0,12"
                           Text="Surface firmware (including thermal-table and fan-curve fixes) ships through Windows Update. Check optional updates too. The Surface app reports device warranty and runs hardware diagnostics."/>
                <StackPanel Orientation="Horizontal">
                  <Button x:Name="BtnOpenWU" Content="Open Windows Update" Style="{StaticResource AccentBtn}" Margin="0,0,8,0"/>
                  <Button x:Name="BtnOpenStore" Content="Get the Surface app"/>
                </StackPanel>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}" Width="555">
              <StackPanel>
                <TextBlock Style="{StaticResource CardTitle}" Text="NETWORK"/>
                <TextBlock Style="{StaticResource CardNote}" Margin="0,0,0,12"
                           Text="Flushes the DNS resolver cache. Harmless, occasionally fixes odd connectivity after years of use."/>
                <Button x:Name="BtnFlushDns" Content="Flush DNS cache" HorizontalAlignment="Left"/>
              </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}" Width="555">
              <StackPanel>
                <TextBlock Style="{StaticResource CardTitle}" Text="PHYSICAL CARE (MANUAL)"/>
                <TextBlock Style="{StaticResource CardNote}"
                           Text="Software can only go so far on a heavily-used device: blow out the perimeter vent channel with short bursts of compressed air (device off), keep the kickstand fully open under load (it is part of the heat path), use hard surfaces, and if the battery report shows heavy wear or any swelling, replace the battery. For a full refresh, a clean install of Windows 11 via the Media Creation Tool beats Reset this PC."/>
              </StackPanel>
            </Border>
          </WrapPanel>
        </ScrollViewer>
      </TabItem>
    </TabControl>

    <GridSplitter Grid.Row="2" Height="5" HorizontalAlignment="Stretch" Background="{StaticResource Mantle}"
                  ResizeBehavior="PreviousAndNext" ResizeDirection="Rows" Margin="12,6,12,0"/>

    <!-- console -->
    <Border Grid.Row="3" Background="{StaticResource Crust}" Margin="12,4,12,8" CornerRadius="8" Padding="10,8">
      <Grid>
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="*"/>
        </Grid.RowDefinitions>
        <DockPanel Grid.Row="0" Margin="2,0,2,6">
          <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
            <Button x:Name="BtnLogCopy" Content="Copy" Style="{StaticResource SmallBtn}" Margin="0,0,6,0"/>
            <Button x:Name="BtnLogClear" Content="Clear" Style="{StaticResource SmallBtn}"/>
          </StackPanel>
          <TextBlock Text="CONSOLE" FontSize="11" FontWeight="Bold" Foreground="{StaticResource Sub}" VerticalAlignment="Center"/>
        </DockPanel>
        <TextBox x:Name="TxtLog" Grid.Row="1" IsReadOnly="True" FontFamily="Consolas" FontSize="12"
                 Background="Transparent" BorderThickness="0" TextWrapping="NoWrap"
                 VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto"/>
      </Grid>
    </Border>

    <!-- status bar -->
    <Border Grid.Row="4" Background="{StaticResource Mantle}" Padding="14,6">
      <DockPanel>
        <TextBlock x:Name="TxtVer" DockPanel.Dock="Right" FontSize="11" Foreground="{StaticResource Sub}" Text="SurfaceMedic v0.2.0"/>
        <TextBlock x:Name="TxtStatus" FontSize="11" Foreground="{StaticResource Sub}" Text="Ready"/>
      </DockPanel>
    </Border>

    <!-- toast overlay -->
    <Border x:Name="ToastBorder" Grid.Row="0" Grid.RowSpan="5" Panel.ZIndex="99"
            HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,70,24,0"
            CornerRadius="8" Padding="16,10" Visibility="Collapsed" Background="{StaticResource Green}">
      <TextBlock x:Name="ToastText" FontWeight="SemiBold" Foreground="{StaticResource Crust}" Text=""/>
    </Border>
  </Grid>
</Window>
'@

$script:Window = [Windows.Markup.XamlReader]::Parse($xaml)

# dark title bar
$script:Window.Add_SourceInitialized({
    try {
        $hwnd = (New-Object System.Windows.Interop.WindowInteropHelper($script:Window)).Handle
        $val = 1
        [void][SurfaceMedic.Dwm]::DwmSetWindowAttribute($hwnd, 20, [ref]$val, 4)
    } catch {}
})

# ------------------------------------------------------------------ control refs
$script:UI = @{}
$controlNames = @(
    'BusyBar','AdminBadge','MainTabs','ToastBorder','ToastText',
    'BtnDashRefresh','CardDevice','CardOs','CardBattery','CardStorage','CardDisk','CardThermal',
    'BtnDays7','BtnDays30','BtnDays90','BtnScanEvents','BtnExportEvents','TxtEventSummary','GridEvents',
    'TxtTurbo','BtnCap99','BtnCap100','TxtOverlay','BtnOvlEff','BtnOvlBal','BtnOvlPerf','TxtPlan','BtnRefreshPower','BtnBattReport',
    'BtnToolHwinfo','BtnToolCdi','BtnToolLhm','BtnToolPt',
    'TxtSearch','BtnSearch','BtnInstallSel','GridPackages','BtnCheckUpd','BtnUpgSel','BtnUpgAll','GridUpdates',
    'BtnCleanTemp','BtnSfc','BtnDismScan','BtnDismRestore','BtnCompClean','BtnFlushDns','BtnOpenWU','BtnOpenStore',
    'TxtLog','BtnLogClear','BtnLogCopy','TxtStatus','TxtVer'
)
foreach ($n in $controlNames) { $script:UI[$n] = $script:Window.FindName($n) }

if (-not $script:IsAdmin) {
    $script:UI.AdminBadge.Text = 'Limited (not elevated)'
    $script:UI.AdminBadge.Foreground = $script:Window.Resources['Yellow']
}

# ------------------------------------------------------------------ shared state
$script:Sync = [hashtable]::Synchronized(@{})
$script:Sync.LogQueue = New-Object 'System.Collections.Concurrent.ConcurrentQueue[object]'
$script:Sync.Window   = $script:Window
$script:Sync.UI       = $script:UI

# helper functions dot-sourced into every worker runspace
$script:Sync.Helpers = @'
function Send-Log {
    param($sync, [string]$m, [string]$l = 'INFO')
    $sync.LogQueue.Enqueue([pscustomobject]@{ Level = $l; Message = $m; Kind = ''; Time = (Get-Date).ToString('HH:mm:ss') })
}
function Send-Toast {
    param($sync, [string]$m, [string]$k = 'ok')
    $sync.LogQueue.Enqueue([pscustomobject]@{ Level = 'TOAST'; Message = $m; Kind = $k; Time = (Get-Date).ToString('HH:mm:ss') })
}
function Invoke-ToolStream {
    param($sync, [string]$File, [string[]]$ToolArgs, [switch]$QuietProgress)
    Send-Log $sync ("> {0} {1}" -f $File, ($ToolArgs -join ' ')) 'CMD'
    $cmd = Get-Command $File -ErrorAction SilentlyContinue
    if (-not $cmd) { Send-Log $sync ("{0} was not found on PATH." -f $File) 'ERROR'; return -1 }
    & $File @ToolArgs 2>&1 | ForEach-Object {
        $s = ([string]$_) -replace "`0", '' -replace "`b", ''
        foreach ($part in ($s -split "`r")) {
            $p = $part.TrimEnd()
            if ($p -eq '') { continue }
            if ($QuietProgress) {
                if ($p -match '^[\s\-\\|/\u2580-\u25FF]+$') { continue }
                if ($p -match '^\s*[\[\u2580-\u25FF]' -and $p -match '[%\]]\s*$') { continue }
                if ($p -match '^\s*\d+(\.\d+)?\s*(KB|MB|GB)\s*/\s*\d+') { continue }
            }
            Send-Log $sync $p 'OUT'
        }
    }
    return $LASTEXITCODE
}
function Invoke-WingetCapture {
    param($sync, [string[]]$WgArgs)
    $cmd = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $cmd) {
        Send-Log $sync 'winget is not installed. Install "App Installer" from the Microsoft Store.' 'ERROR'
        Send-Toast $sync 'winget not found' 'err'
        return $null
    }
    Send-Log $sync ("> winget {0}" -f ($WgArgs -join ' ')) 'CMD'
    $out = & winget @WgArgs 2>&1 | ForEach-Object { ([string]$_) -replace "`b", '' }
    return $out
}
function ConvertFrom-WingetTable {
    param([string[]]$Lines)
    if (-not $Lines) { return @() }
    $clean = @($Lines | ForEach-Object { ([string]$_).TrimEnd() } | Where-Object { $_ -ne '' })
    $hdrIdx = -1
    for ($i = 0; $i -lt $clean.Count; $i++) {
        if ($clean[$i] -match '^\s*Name\s+Id\s+') { $hdrIdx = $i; break }
    }
    if ($hdrIdx -lt 0) { return @() }
    $header = $clean[$hdrIdx]
    $cols = @()
    foreach ($cname in @('Name', 'Id', 'Version', 'Match', 'Available', 'Source')) {
        $pos = $header.IndexOf($cname)
        if ($pos -ge 0) { $cols += [pscustomobject]@{ Name = $cname; Start = $pos } }
    }
    $cols = @($cols | Sort-Object Start)
    $rows = @()
    for ($i = $hdrIdx + 1; $i -lt $clean.Count; $i++) {
        $line = $clean[$i]
        if ($line -match '^-{4,}$') { continue }
        if ($line -match '^\s*Name\s+Id\s+') { continue }
        if ($line -match '^\d+ (package|upgrade)') { continue }
        if ($line -match 'upgrades? available' -or $line -match 'explicit targeting') { continue }
        $obj = [ordered]@{}
        for ($c = 0; $c -lt $cols.Count; $c++) {
            $start = $cols[$c].Start
            $end = if ($c + 1 -lt $cols.Count) { $cols[$c + 1].Start } else { $line.Length }
            $val = ''
            if ($start -lt $line.Length) {
                $len = [Math]::Min($end, $line.Length) - $start
                if ($len -gt 0) { $val = $line.Substring($start, $len).Trim() }
            }
            $obj[$cols[$c].Name] = $val
        }
        if ($obj['Name'] -and $obj['Id']) { $rows += [pscustomobject]$obj }
    }
    return $rows
}
function Get-TurboStatus {
    $txt = (powercfg /q scheme_current sub_processor PROCTHROTTLEMAX 2>&1) -join "`n"
    $ac = $null; $dc = $null
    if ($txt -match 'Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)') { $ac = [Convert]::ToInt32($Matches[1], 16) }
    if ($txt -match 'Current DC Power Setting Index:\s*0x([0-9a-fA-F]+)') { $dc = [Convert]::ToInt32($Matches[1], 16) }
    if ($null -eq $ac) { return 'Unable to read the processor cap (PROCTHROTTLEMAX).' }
    $state = 'Turbo Boost is ENABLED - full clocks, runs hotter.'
    if ($ac -le 99 -and $dc -le 99) { $state = 'Turbo Boost is DISABLED - cool and quiet mode.' }
    elseif ($ac -le 99 -or $dc -le 99) { $state = 'Turbo Boost is partially capped (AC and DC differ).' }
    return ("Plugged in (AC): {0}%    On battery (DC): {1}%`n{2}" -f $ac, $dc, $state)
}
function Get-PowerOverlayText {
    $t = (powercfg /getactiveoverlayscheme 2>&1) -join ' '
    $name = 'Balanced (no overlay active)'
    if ($t -match '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})') {
        $guid = $Matches[1].ToLower()
        switch ($guid) {
            '961cc777-2547-4f9d-8174-7d86181b8a7a' { $name = 'Best power efficiency' }
            '3af9b8d9-7c97-431d-ad78-34a8bfea439f' { $name = 'Better performance' }
            'ded574b5-45a0-4f42-8737-46345c09c238' { $name = 'Best performance' }
            '00000000-0000-0000-0000-000000000000' { $name = 'Balanced (default)' }
            default { $name = "Custom overlay ($guid)" }
        }
    }
    return ("Active power mode: {0}" -f $name)
}
'@

# worker wrapper: dot-sources helpers, runs the work block, reports completion
$script:WrapperText = @'
param($sync, $workText, $name, $extra)
. ([scriptblock]::Create($sync.Helpers))
$log = { param($m, $l = 'INFO') Send-Log $sync $m $l }
try {
    $work = [scriptblock]::Create($workText)
    & $work $sync $log $extra
} catch {
    Send-Log $sync ("{0} failed: {1}" -f $name, $_.Exception.Message) 'ERROR'
    Send-Toast $sync ("{0} failed" -f $name) 'err'
} finally {
    $sync.LogQueue.Enqueue([pscustomobject]@{ Level = 'JOBDONE'; Message = $name; Kind = ''; Time = (Get-Date).ToString('HH:mm:ss') })
}
'@

$script:Pool = [runspacefactory]::CreateRunspacePool(1, 4)
$script:Pool.Open()
$script:Jobs = New-Object System.Collections.ArrayList
$script:Running = 0
$script:ScanDays = 30

# ------------------------------------------------------------------ UI helpers
function Append-Log {
    param([string]$Line)
    $tb = $script:UI.TxtLog
    $tb.AppendText($Line + [Environment]::NewLine)
    if ($tb.Text.Length -gt 400000) { $tb.Text = $tb.Text.Substring($tb.Text.Length - 200000) }
    $tb.ScrollToEnd()
}

function Update-BusyUI {
    if ($script:Running -gt 0) {
        $script:UI.BusyBar.Visibility = 'Visible'
        $script:UI.BusyBar.IsIndeterminate = $true
        $script:UI.TxtStatus.Text = "Working on $($script:Running) task(s)..."
    } else {
        $script:UI.BusyBar.IsIndeterminate = $false
        $script:UI.BusyBar.Visibility = 'Collapsed'
        $script:UI.TxtStatus.Text = 'Ready'
    }
}

function Show-Toast {
    param([string]$Msg, [string]$Kind = 'ok')
    $brushKey = 'Green'
    if ($Kind -eq 'err') { $brushKey = 'Red' }
    elseif ($Kind -eq 'info') { $brushKey = 'Blue' }
    $script:UI.ToastBorder.Background = $script:Window.Resources[$brushKey]
    $script:UI.ToastText.Text = $Msg
    $script:UI.ToastBorder.Visibility = 'Visible'
    $script:ToastTimer.Stop()
    $script:ToastTimer.Start()
}

function Start-Async {
    param([string]$Name, [scriptblock]$Work, $Extra = $null)
    $script:Running++
    Update-BusyUI
    Append-Log ("[{0}] [TASK] {1} started" -f (Get-Date -Format 'HH:mm:ss'), $Name)
    $ps = [powershell]::Create()
    $ps.RunspacePool = $script:Pool
    [void]$ps.AddScript($script:WrapperText).AddArgument($script:Sync).AddArgument($Work.ToString()).AddArgument($Name).AddArgument($Extra)
    $handle = $ps.BeginInvoke()
    [void]$script:Jobs.Add([pscustomobject]@{ PS = $ps; Handle = $handle; Name = $Name })
}

# toast auto-hide
$script:ToastTimer = New-Object System.Windows.Threading.DispatcherTimer
$script:ToastTimer.Interval = [TimeSpan]::FromSeconds(2.8)
$script:ToastTimer.Add_Tick({
    $script:UI.ToastBorder.Visibility = 'Collapsed'
    $script:ToastTimer.Stop()
})

# queue pump: drains worker output onto the UI thread
$script:PumpTimer = New-Object System.Windows.Threading.DispatcherTimer
$script:PumpTimer.Interval = [TimeSpan]::FromMilliseconds(150)
$script:PumpTimer.Add_Tick({
    $item = $null
    while ($script:Sync.LogQueue.TryDequeue([ref]$item)) {
        switch ($item.Level) {
            'JOBDONE' {
                $script:Running = [Math]::Max(0, $script:Running - 1)
                Append-Log ("[{0}] [TASK] {1} finished" -f $item.Time, $item.Message)
                Update-BusyUI
            }
            'TOAST' { Show-Toast $item.Message $item.Kind }
            default { Append-Log ("[{0}] [{1}] {2}" -f $item.Time, $item.Level, $item.Message) }
        }
    }
    for ($i = $script:Jobs.Count - 1; $i -ge 0; $i--) {
        $j = $script:Jobs[$i]
        if ($j.Handle.IsCompleted) {
            try { [void]$j.PS.EndInvoke($j.Handle) } catch {}
            $j.PS.Dispose()
            $script:Jobs.RemoveAt($i)
        }
    }
})
$script:PumpTimer.Start()

# ------------------------------------------------------------------ work blocks
$script:WorkDashboard = {
    param($sync, $log, $extra)
    & $log 'Collecting system inventory...'

    # device
    $devText = 'Unavailable.'
    try {
        $cs = Get-CimInstance Win32_ComputerSystem
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        $ram = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
        $devText = "{0} {1}`nCPU: {2}`nRAM: {3} GB" -f $cs.Manufacturer, $cs.Model, $cpu.Name.Trim(), $ram
    } catch { & $log "Device query failed: $($_.Exception.Message)" 'WARN' }

    # OS
    $osText = 'Unavailable.'
    try {
        $os = Get-CimInstance Win32_OperatingSystem
        $ver = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue
        $up = (Get-Date) - $os.LastBootUpTime
        $disp = ''
        if ($ver -and $ver.DisplayVersion) { $disp = $ver.DisplayVersion }
        $osText = "{0} {1} (build {2})`nInstalled: {3:yyyy-MM-dd}`nUptime: {4}d {5}h {6}m" -f `
            $os.Caption, $disp, $os.BuildNumber, $os.InstallDate, [int]$up.Days, $up.Hours, $up.Minutes
    } catch { & $log "OS query failed: $($_.Exception.Message)" 'WARN' }

    # battery
    $batText = 'No battery detected (or reporting unavailable).'
    try {
        $tmp = Join-Path $env:TEMP 'surfacemedic-batt.xml'
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
        $null = powercfg /batteryreport /output "$tmp" /XML 2>&1
        if (Test-Path $tmp) {
            $x = [xml](Get-Content $tmp -Raw)
            $bat = $x.GetElementsByTagName('Battery') | Select-Object -First 1
            if ($bat -and $bat.DesignCapacity) {
                $design = [double]$bat.DesignCapacity
                $full = [double]$bat.FullChargeCapacity
                $cyc = ''
                if ($bat.CycleCount) { $cyc = $bat.CycleCount }
                $wear = 0
                if ($design -gt 0) { $wear = [math]::Round((1 - ($full / $design)) * 100, 1) }
                $verdict = 'Healthy.'
                if ($wear -ge 35) { $verdict = 'Heavily worn - replacement recommended.' }
                elseif ($wear -ge 20) { $verdict = 'Noticeably worn - consider replacement.' }
                $batText = "Design: {0:N0} mWh    Full charge: {1:N0} mWh`nWear: {2}%    Cycles: {3}`n{4}" -f $design, $full, $wear, $cyc, $verdict
            }
            Remove-Item $tmp -Force -ErrorAction SilentlyContinue
        }
    } catch { & $log "Battery query failed: $($_.Exception.Message)" 'WARN' }

    # storage
    $stoText = 'Unavailable.'
    try {
        $c = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
        $total = [math]::Round($c.Size / 1GB, 1)
        $free = [math]::Round($c.FreeSpace / 1GB, 1)
        $used = [math]::Round($total - $free, 1)
        $pct = 0
        if ($c.Size -gt 0) { $pct = [math]::Round(($used / $total) * 100, 0) }
        $note = ''
        if ($pct -ge 90) { $note = "`nDrive is nearly full - a full SSD runs slower and warmer." }
        $stoText = "C: {0} GB used of {1} GB ({2}%)`n{3} GB free{4}" -f $used, $total, $pct, $free, $note
    } catch { & $log "Storage query failed: $($_.Exception.Message)" 'WARN' }

    # disk health
    $dskText = 'Unavailable.'
    try {
        $lines = @()
        foreach ($pd in @(Get-PhysicalDisk -ErrorAction SilentlyContinue)) {
            $l = "{0} [{1}] - {2}" -f $pd.FriendlyName, $pd.MediaType, $pd.HealthStatus
            $rc = $null
            try { $rc = $pd | Get-StorageReliabilityCounter -ErrorAction Stop } catch {}
            if ($rc) {
                $bits = @()
                if ($null -ne $rc.Wear -and $rc.Wear -gt 0) { $bits += "wear $($rc.Wear)%" }
                if ($null -ne $rc.Temperature -and $rc.Temperature -gt 0) { $bits += "temp $($rc.Temperature) $([char]176)C" }
                if ($bits.Count) { $l += ' (' + ($bits -join ', ') + ')' }
            }
            $lines += $l
        }
        if ($lines.Count) { $dskText = $lines -join "`n" }
    } catch { & $log "Disk query failed: $($_.Exception.Message)" 'WARN' }

    # thermal snapshot
    $thText = 'Unavailable.'
    try {
        $since7 = (Get-Date).AddDays(-7)
        $c125 = 0; $c37 = 0
        try { $c125 = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-Kernel-Power'; Id = 125; StartTime = $since7 } -ErrorAction Stop).Count } catch {}
        try { $c37 = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-Kernel-Processor-Power'; Id = 37; StartTime = $since7 } -ErrorAction Stop).Count } catch {}
        $zoneTxt = 'Thermal zone sensors not exposed by this firmware.'
        try {
            $z = Get-CimInstance -Namespace root/wmi -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction Stop
            $temps = @($z | ForEach-Object { [math]::Round(($_.CurrentTemperature / 10) - 273.15, 1) } | Where-Object { $_ -gt 0 })
            if ($temps.Count) {
                $zoneTxt = 'Thermal zones now: ' + (($temps | ForEach-Object { "$_ $([char]176)C" }) -join ', ')
            }
        } catch {}
        $verdict = 'No throttling recorded this week.'
        if (($c125 + $c37) -gt 0) { $verdict = 'Device IS throttling - see the Thermal Events tab.' }
        $thText = "Throttle engagements (Kernel-Power 125): {0}`nFirmware speed caps (Processor-Power 37): {1}`n{2}`n{3}" -f $c125, $c37, $zoneTxt, $verdict
    } catch { & $log "Thermal query failed: $($_.Exception.Message)" 'WARN' }

    $sync.Window.Dispatcher.Invoke([action]{
        $sync.UI.CardDevice.Text  = $devText
        $sync.UI.CardOs.Text      = $osText
        $sync.UI.CardBattery.Text = $batText
        $sync.UI.CardStorage.Text = $stoText
        $sync.UI.CardDisk.Text    = $dskText
        $sync.UI.CardThermal.Text = $thText
    })
    & $log 'Dashboard updated.'
}

$script:WorkPowerStatus = {
    param($sync, $log, $extra)
    $turbo = Get-TurboStatus
    $ovl = Get-PowerOverlayText
    $plan = ((powercfg /getactivescheme 2>&1) -join ' ') -replace 'Power Scheme GUID:\s*', ''
    $sync.Window.Dispatcher.Invoke([action]{
        $sync.UI.TxtTurbo.Text = $turbo
        $sync.UI.TxtOverlay.Text = $ovl
        $sync.UI.TxtPlan.Text = $plan.Trim()
    })
    & $log 'Power status refreshed.'
}

$script:WorkSetCap = {
    param($sync, $log, $extra)
    $v = [int]$extra
    $null = Invoke-ToolStream $sync 'powercfg' @('/setacvalueindex', 'scheme_current', 'sub_processor', 'PROCTHROTTLEMAX', "$v")
    $null = Invoke-ToolStream $sync 'powercfg' @('/setdcvalueindex', 'scheme_current', 'sub_processor', 'PROCTHROTTLEMAX', "$v")
    $null = Invoke-ToolStream $sync 'powercfg' @('/setactive', 'scheme_current')
    $turbo = Get-TurboStatus
    $sync.Window.Dispatcher.Invoke([action]{ $sync.UI.TxtTurbo.Text = $turbo })
    if ($v -le 99) { Send-Toast $sync 'CPU capped at 99% - Turbo Boost off, expect 10-15 degrees cooler' 'ok' }
    else { Send-Toast $sync 'CPU restored to 100% - Turbo Boost enabled' 'ok' }
}

$script:WorkSetOverlay = {
    param($sync, $log, $extra)
    $null = Invoke-ToolStream $sync 'powercfg' @('/overlaysetactive', "$extra")
    $code = $LASTEXITCODE
    $ovl = Get-PowerOverlayText
    $sync.Window.Dispatcher.Invoke([action]{ $sync.UI.TxtOverlay.Text = $ovl })
    Send-Toast $sync 'Power mode updated' 'ok'
}

$script:WorkBatteryReport = {
    param($sync, $log, $extra)
    $out = Join-Path ([Environment]::GetFolderPath('Desktop')) 'SurfaceMedic-battery-report.html'
    $null = Invoke-ToolStream $sync 'powercfg' @('/batteryreport', '/output', "$out")
    if (Test-Path $out) {
        Start-Process $out
        Send-Toast $sync 'Battery report generated and opened' 'ok'
    } else {
        Send-Toast $sync 'Battery report failed (no battery on this device?)' 'err'
    }
}

$script:WorkScanEvents = {
    param($sync, $log, $extra)
    $days = [int]$extra
    $since = (Get-Date).AddDays(-$days)
    & $log ("Scanning System log for thermal/throttle events since {0:yyyy-MM-dd}..." -f $since)
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    $rows = New-Object 'System.Collections.Generic.List[object]'
    $c125 = 0; $c37 = 0; $cWhea = 0; $cKw = 0

    $targets = @(
        @{ P = 'Microsoft-Windows-Kernel-Power';           I = 125;   Tag = 'throttle' },
        @{ P = 'Microsoft-Windows-Kernel-Processor-Power'; I = 37;    Tag = 'fwcap' },
        @{ P = 'Microsoft-Windows-WHEA-Logger';            I = $null; Tag = 'whea' }
    )
    foreach ($t in $targets) {
        $flt = @{ LogName = 'System'; ProviderName = $t.P; StartTime = $since }
        if ($t.I) { $flt['Id'] = $t.I }
        $evts = @()
        try { $evts = @(Get-WinEvent -FilterHashtable $flt -ErrorAction Stop) } catch {}
        foreach ($e in $evts) {
            if (-not $seen.Add("$($e.RecordId)")) { continue }
            $msg = ''
            if ($e.Message) { $msg = ($e.Message -replace '\s+', ' ').Trim() }
            if ($msg.Length -gt 300) { $msg = $msg.Substring(0, 300) + '...' }
            $r = New-Object SurfaceMedic.EvtRow
            $r.SortTime = $e.TimeCreated
            $r.Time = $e.TimeCreated.ToString('yyyy-MM-dd HH:mm')
            $r.Level = "$($e.LevelDisplayName)"
            $r.Provider = "$($e.ProviderName)" -replace '^Microsoft-Windows-', ''
            $r.Id = "$($e.Id)"
            $r.Message = $msg
            $rows.Add($r)
            switch ($t.Tag) {
                'throttle' { $c125++ }
                'fwcap'    { $c37++ }
                'whea'     { $cWhea++ }
            }
        }
    }

    # keyword sweep over warnings/errors for anything the targeted queries missed
    try {
        $sweep = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; Level = 1, 2, 3; StartTime = $since } -MaxEvents 3000 -ErrorAction Stop)
        foreach ($e in $sweep) {
            if (-not $e.Message) { continue }
            if ($e.Message -notmatch '(?i)thermal|throttl|overheat|temperature') { continue }
            if (-not $seen.Add("$($e.RecordId)")) { continue }
            $msg = ($e.Message -replace '\s+', ' ').Trim()
            if ($msg.Length -gt 300) { $msg = $msg.Substring(0, 300) + '...' }
            $r = New-Object SurfaceMedic.EvtRow
            $r.SortTime = $e.TimeCreated
            $r.Time = $e.TimeCreated.ToString('yyyy-MM-dd HH:mm')
            $r.Level = "$($e.LevelDisplayName)"
            $r.Provider = "$($e.ProviderName)" -replace '^Microsoft-Windows-', ''
            $r.Id = "$($e.Id)"
            $r.Message = $msg
            $rows.Add($r)
            $cKw++
        }
    } catch {}

    $sorted = @($rows | Sort-Object SortTime -Descending)
    $summary = "{0} events in the last {1} days: {2} thermal throttle engagements (Kernel-Power 125), {3} firmware speed caps (Processor-Power 37), {4} WHEA hardware errors, {5} thermal-keyword warnings." -f `
        $sorted.Count, $days, $c125, $c37, $cWhea, $cKw
    & $log $summary
    $sync.Window.Dispatcher.Invoke([action]{
        $sync.UI.GridEvents.ItemsSource = $sorted
        $sync.UI.TxtEventSummary.Text = $summary
    })
    if ($sorted.Count -eq 0) { Send-Toast $sync "No thermal events in the last $days days" 'ok' }
    else { Send-Toast $sync "$($sorted.Count) thermal/throttle events found" 'info' }
}

$script:WorkWingetSearch = {
    param($sync, $log, $extra)
    $lines = Invoke-WingetCapture $sync @('search', "$extra", '--accept-source-agreements')
    if ($null -eq $lines) { return }
    $parsed = @(ConvertFrom-WingetTable $lines)
    $items = New-Object 'System.Collections.Generic.List[object]'
    foreach ($p in $parsed) {
        $o = New-Object SurfaceMedic.PkgRow
        $o.Name = "$($p.Name)"; $o.Id = "$($p.Id)"; $o.Version = "$($p.Version)"; $o.Source = "$($p.Source)"
        $items.Add($o)
    }
    & $log ("Found {0} package(s) for '{1}'." -f $items.Count, $extra)
    $sync.Window.Dispatcher.Invoke([action]{ $sync.UI.GridPackages.ItemsSource = $items })
    if ($items.Count -eq 0) { Send-Toast $sync "No packages found for '$extra'" 'info' }
}

$script:WorkWingetInstall = {
    param($sync, $log, $extra)
    foreach ($id in @($extra)) {
        & $log "Installing $id ..."
        $code = Invoke-ToolStream $sync 'winget' @('install', '--id', "$id", '--exact', '--silent', '--accept-package-agreements', '--accept-source-agreements') -QuietProgress
        if ($code -eq 0) { Send-Toast $sync "Installed $id" 'ok' }
        else { Send-Toast $sync "Install of $id exited with code $code" 'err' }
    }
}

$script:WorkWingetUpdates = {
    param($sync, $log, $extra)
    $lines = Invoke-WingetCapture $sync @('upgrade', '--include-unknown', '--accept-source-agreements')
    if ($null -eq $lines) { return }
    $parsed = @(ConvertFrom-WingetTable $lines)
    $items = New-Object 'System.Collections.Generic.List[object]'
    foreach ($p in $parsed) {
        $o = New-Object SurfaceMedic.PkgRow
        $o.Name = "$($p.Name)"; $o.Id = "$($p.Id)"; $o.Version = "$($p.Version)"; $o.Source = "$($p.Source)"
        $av = $p.PSObject.Properties['Available']
        if ($av) { $o.Available = "$($av.Value)" }
        $items.Add($o)
    }
    & $log ("{0} package(s) have updates available." -f $items.Count)
    $sync.Window.Dispatcher.Invoke([action]{ $sync.UI.GridUpdates.ItemsSource = $items })
    if ($items.Count -eq 0) { Send-Toast $sync 'Everything is up to date' 'ok' }
    else { Send-Toast $sync "$($items.Count) update(s) available" 'info' }
}

$script:WorkWingetUpgrade = {
    param($sync, $log, $extra)
    foreach ($id in @($extra)) {
        & $log "Upgrading $id ..."
        $code = Invoke-ToolStream $sync 'winget' @('upgrade', '--id', "$id", '--exact', '--silent', '--accept-package-agreements', '--accept-source-agreements') -QuietProgress
        if ($code -eq 0) { Send-Toast $sync "Upgraded $id" 'ok' }
        else { Send-Toast $sync "Upgrade of $id exited with code $code" 'err' }
    }
}

$script:WorkWingetUpgradeAll = {
    param($sync, $log, $extra)
    $code = Invoke-ToolStream $sync 'winget' @('upgrade', '--all', '--silent', '--accept-package-agreements', '--accept-source-agreements') -QuietProgress
    if ($code -eq 0) { Send-Toast $sync 'All packages upgraded' 'ok' }
    else { Send-Toast $sync "winget upgrade --all exited with code $code" 'err' }
}

$script:WorkCleanTemp = {
    param($sync, $log, $extra)
    $targets = @($env:TEMP, (Join-Path $env:windir 'Temp'))
    $totalFreed = 0
    foreach ($t in $targets) {
        if (-not (Test-Path $t)) { continue }
        $before = [long](Get-ChildItem $t -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
        Get-ChildItem $t -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
        $after = [long](Get-ChildItem $t -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
        $freed = [Math]::Max(0, $before - $after)
        $totalFreed += $freed
        & $log ("{0}: freed {1:N1} MB" -f $t, ($freed / 1MB))
    }
    Send-Toast $sync ("Freed {0:N1} MB of temp files" -f ($totalFreed / 1MB)) 'ok'
}

$script:WorkSfc = {
    param($sync, $log, $extra)
    $code = Invoke-ToolStream $sync 'sfc' @('/scannow')
    if ($code -eq 0) { Send-Toast $sync 'SFC completed' 'ok' } else { Send-Toast $sync "SFC exited with code $code" 'err' }
}

$script:WorkDismScan = {
    param($sync, $log, $extra)
    $code = Invoke-ToolStream $sync 'dism' @('/Online', '/Cleanup-Image', '/ScanHealth') -QuietProgress
    if ($code -eq 0) { Send-Toast $sync 'DISM ScanHealth completed' 'ok' } else { Send-Toast $sync "DISM exited with code $code" 'err' }
}

$script:WorkDismRestore = {
    param($sync, $log, $extra)
    $code = Invoke-ToolStream $sync 'dism' @('/Online', '/Cleanup-Image', '/RestoreHealth') -QuietProgress
    if ($code -eq 0) { Send-Toast $sync 'DISM RestoreHealth completed' 'ok' } else { Send-Toast $sync "DISM exited with code $code" 'err' }
}

$script:WorkCompClean = {
    param($sync, $log, $extra)
    $code = Invoke-ToolStream $sync 'dism' @('/Online', '/Cleanup-Image', '/StartComponentCleanup') -QuietProgress
    if ($code -eq 0) { Send-Toast $sync 'Component cleanup completed' 'ok' } else { Send-Toast $sync "DISM exited with code $code" 'err' }
}

$script:WorkFlushDns = {
    param($sync, $log, $extra)
    $code = Invoke-ToolStream $sync 'ipconfig' @('/flushdns')
    if ($code -eq 0) { Send-Toast $sync 'DNS cache flushed' 'ok' } else { Send-Toast $sync "ipconfig exited with code $code" 'err' }
}

$script:WorkWingetVersion = {
    param($sync, $log, $extra)
    $cmd = Get-Command winget -ErrorAction SilentlyContinue
    if ($cmd) {
        $v = (& winget --version 2>&1) -join ''
        & $log "winget $v detected."
    } else {
        & $log 'winget is NOT installed. The Software tab needs "App Installer" from the Microsoft Store.' 'WARN'
    }
}

# ------------------------------------------------------------------ handlers
function Select-Days {
    param([int]$d)
    $script:ScanDays = $d
    $map = @{ 7 = $script:UI.BtnDays7; 30 = $script:UI.BtnDays30; 90 = $script:UI.BtnDays90 }
    foreach ($k in $map.Keys) {
        if ($k -eq $d) {
            $map[$k].Background = $script:Window.Resources['Mauve']
            $map[$k].Foreground = $script:Window.Resources['Crust']
        } else {
            $map[$k].Background = $script:Window.Resources['Surf0']
            $map[$k].Foreground = $script:Window.Resources['TextBr']
        }
    }
}

function Start-WingetSearch {
    $q = $script:UI.TxtSearch.Text.Trim()
    if (-not $q) { Show-Toast 'Type a package name to search' 'info'; return }
    Start-Async "winget search '$q'" $script:WorkWingetSearch $q
}

$script:UI.BtnDashRefresh.Add_Click({ Start-Async 'Dashboard refresh' $script:WorkDashboard })

$script:UI.BtnDays7.Add_Click({ Select-Days 7 })
$script:UI.BtnDays30.Add_Click({ Select-Days 30 })
$script:UI.BtnDays90.Add_Click({ Select-Days 90 })
$script:UI.BtnScanEvents.Add_Click({ Start-Async "Thermal event scan ($($script:ScanDays)d)" $script:WorkScanEvents $script:ScanDays })
$script:UI.BtnExportEvents.Add_Click({
    $items = $script:UI.GridEvents.ItemsSource
    if (-not $items -or @($items).Count -eq 0) { Show-Toast 'Nothing to export - run a scan first' 'info'; return }
    $path = Join-Path ([Environment]::GetFolderPath('Desktop')) ("SurfaceMedic-events-{0:yyyyMMdd-HHmmss}.csv" -f (Get-Date))
    @($items) | Select-Object Time, Level, Provider, Id, Message | Export-Csv $path -NoTypeInformation -Encoding UTF8
    Show-Toast "Exported to $path" 'ok'
})

$script:UI.BtnCap99.Add_Click({ Start-Async 'Cap CPU at 99%' $script:WorkSetCap 99 })
$script:UI.BtnCap100.Add_Click({ Start-Async 'Restore CPU to 100%' $script:WorkSetCap 100 })
$script:UI.BtnOvlEff.Add_Click({ Start-Async 'Set power mode: Best efficiency' $script:WorkSetOverlay '961cc777-2547-4f9d-8174-7d86181b8a7a' })
$script:UI.BtnOvlBal.Add_Click({ Start-Async 'Set power mode: Balanced' $script:WorkSetOverlay '00000000-0000-0000-0000-000000000000' })
$script:UI.BtnOvlPerf.Add_Click({ Start-Async 'Set power mode: Best performance' $script:WorkSetOverlay 'ded574b5-45a0-4f42-8737-46345c09c238' })
$script:UI.BtnBattReport.Add_Click({ Start-Async 'Battery report' $script:WorkBatteryReport })
$script:UI.BtnRefreshPower.Add_Click({ Start-Async 'Power status refresh' $script:WorkPowerStatus })

$script:UI.BtnToolHwinfo.Add_Click({ Start-Async 'Install HWiNFO64' $script:WorkWingetInstall @('REALiX.HWiNFO') })
$script:UI.BtnToolCdi.Add_Click({ Start-Async 'Install CrystalDiskInfo' $script:WorkWingetInstall @('CrystalDewWorld.CrystalDiskInfo') })
$script:UI.BtnToolLhm.Add_Click({ Start-Async 'Install LibreHardwareMonitor' $script:WorkWingetInstall @('LibreHardwareMonitor.LibreHardwareMonitor') })
$script:UI.BtnToolPt.Add_Click({ Start-Async 'Install PowerToys' $script:WorkWingetInstall @('Microsoft.PowerToys') })

$script:UI.BtnSearch.Add_Click({ Start-WingetSearch })
$script:UI.TxtSearch.Add_KeyDown({ param($s, $e) if ($e.Key -eq 'Return') { Start-WingetSearch } })
$script:UI.BtnInstallSel.Add_Click({
    $ids = @($script:UI.GridPackages.SelectedItems | ForEach-Object { $_.Id } | Where-Object { $_ })
    if ($ids.Count -eq 0) { Show-Toast 'Select one or more packages first' 'info'; return }
    Start-Async "Install $($ids.Count) package(s)" $script:WorkWingetInstall $ids
})
$script:UI.BtnCheckUpd.Add_Click({ Start-Async 'Check winget updates' $script:WorkWingetUpdates })
$script:UI.BtnUpgSel.Add_Click({
    $ids = @($script:UI.GridUpdates.SelectedItems | ForEach-Object { $_.Id } | Where-Object { $_ })
    if ($ids.Count -eq 0) { Show-Toast 'Select one or more updates first' 'info'; return }
    Start-Async "Upgrade $($ids.Count) package(s)" $script:WorkWingetUpgrade $ids
})
$script:UI.BtnUpgAll.Add_Click({ Start-Async 'Upgrade all packages' $script:WorkWingetUpgradeAll })

$script:UI.BtnCleanTemp.Add_Click({ Start-Async 'Temp file cleanup' $script:WorkCleanTemp })
$script:UI.BtnSfc.Add_Click({ Start-Async 'SFC /scannow' $script:WorkSfc })
$script:UI.BtnDismScan.Add_Click({ Start-Async 'DISM ScanHealth' $script:WorkDismScan })
$script:UI.BtnDismRestore.Add_Click({ Start-Async 'DISM RestoreHealth' $script:WorkDismRestore })
$script:UI.BtnCompClean.Add_Click({ Start-Async 'Component cleanup' $script:WorkCompClean })
$script:UI.BtnFlushDns.Add_Click({ Start-Async 'Flush DNS' $script:WorkFlushDns })
$script:UI.BtnOpenWU.Add_Click({ Start-Process 'ms-settings:windowsupdate'; Show-Toast 'Opening Windows Update' 'info' })
$script:UI.BtnOpenStore.Add_Click({ Start-Process 'ms-windows-store://pdp/?ProductId=9WZDNCRFJB8P'; Show-Toast 'Opening the Surface app in the Store' 'info' })

$script:UI.BtnLogClear.Add_Click({ $script:UI.TxtLog.Clear() })
$script:UI.BtnLogCopy.Add_Click({
    if ($script:UI.TxtLog.Text) { [Windows.Clipboard]::SetText($script:UI.TxtLog.Text); Show-Toast 'Console copied to clipboard' 'ok' }
})

$script:Window.Add_Closed({
    try { $script:PumpTimer.Stop() } catch {}
    try { $script:ToastTimer.Stop() } catch {}
    try { $script:Pool.Close(); $script:Pool.Dispose() } catch {}
})

# ------------------------------------------------------------------ startup
Select-Days $script:ScanDays
Append-Log ("[{0}] [INFO] {1} v{2} - console ready" -f (Get-Date -Format 'HH:mm:ss'), $script:AppName, $script:AppVersion)
if (-not $script:IsAdmin) {
    Append-Log ("[{0}] [WARN] Running without elevation - power tweaks and repair tools need admin" -f (Get-Date -Format 'HH:mm:ss'))
}
Start-Async 'Dashboard refresh' $script:WorkDashboard
Start-Async 'Power status refresh' $script:WorkPowerStatus
Start-Async 'winget detection' $script:WorkWingetVersion

# ------------------------------------------------------------------ smoke mode
function Wait-Dispatcher {
    param([int]$Ms)
    $script:SmokeFrame = New-Object System.Windows.Threading.DispatcherFrame
    $script:SmokeTimer = New-Object System.Windows.Threading.DispatcherTimer
    $script:SmokeTimer.Interval = [TimeSpan]::FromMilliseconds($Ms)
    $script:SmokeTimer.Add_Tick({
        $script:SmokeTimer.Stop()
        $script:SmokeFrame.Continue = $false
    })
    $script:SmokeTimer.Start()
    [System.Windows.Threading.Dispatcher]::PushFrame($script:SmokeFrame)
}

function Save-WindowScreenshot {
    param([string]$Path)
    $root = $script:Window.Content
    $w = [int][Math]::Ceiling($root.ActualWidth)
    $h = [int][Math]::Ceiling($root.ActualHeight)
    if ($w -le 0 -or $h -le 0) { return }
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($w, $h, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($root)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $dir = Split-Path $Path -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    $fs = [System.IO.File]::Open($Path, 'Create')
    try { $enc.Save($fs) } finally { $fs.Close() }
}

if ($Smoke) {
    # test-only: render offscreen without activation, capture, exit
    $script:Window.WindowStartupLocation = 'Manual'
    $script:Window.Left = -20000
    $script:Window.Top = 0
    $script:Window.ShowInTaskbar = $false
    $script:Window.ShowActivated = $false
    $script:Window.Show()
    Wait-Dispatcher 6000

    $shotDir = Join-Path (Split-Path $PSCommandPath -Parent) 'screenshots'
    Save-WindowScreenshot (Join-Path $shotDir 'app.png')
    $script:UI.MainTabs.SelectedIndex = 2
    Wait-Dispatcher 900
    Save-WindowScreenshot (Join-Path $shotDir 'power.png')
    $script:UI.MainTabs.SelectedIndex = 4
    Wait-Dispatcher 900
    Save-WindowScreenshot (Join-Path $shotDir 'maintenance.png')

    $script:Window.Close()
    Write-Output 'SMOKE OK'
    exit 0
} else {
    [void]$script:Window.ShowDialog()
}
