using System.Threading;
using UnityEngine;
using UnityEngine.Serialization;


public interface IVisualDrawer<T>
{
    public void ShowImmediate(T visual);
    public Awaitable ShowAsync(T visual, CancellationToken ct = default);
 
    public void HideImmediate();
    public Awaitable HideAsync(CancellationToken ct = default);
}