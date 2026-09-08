using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>INotifyPropertyChanged reutilizable por los ViewModels.</summary>
public abstract class ObservableObjectBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void OnPropertyChanged(string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
