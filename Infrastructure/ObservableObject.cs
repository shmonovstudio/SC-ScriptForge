using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SC.ScriptForge.Infrastructure;

public abstract class ObservableObject : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        return SetProperty(ref field, value, property);
    }

    protected void Raise([CallerMemberName] string? property = null) =>
        OnPropertyChanged(property);
}
