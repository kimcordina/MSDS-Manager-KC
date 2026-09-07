// UseWPF + UseWindowsForms both inject implicit usings. Without these
// aliases, common types fail CS0104 on Windows Release builds.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using MessageBoxButton = System.Windows.MessageBoxButton;
global using MessageBoxImage = System.Windows.MessageBoxImage;
global using MessageBoxResult = System.Windows.MessageBoxResult;
global using Color = System.Windows.Media.Color;
global using System.IO;
