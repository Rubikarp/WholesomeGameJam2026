using System.Threading;
using UnityEngine;

namespace WorldGame.SceneManagement
{
    public interface ITransitionEffect
    {
        public Awaitable PlayInAsync(CancellationToken ct);
        public void ReactToProgress(float progress);
        public Awaitable PlayOutAsync(CancellationToken ct);
    }
}