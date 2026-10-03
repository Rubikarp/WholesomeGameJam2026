using System.Linq;
using UnityEngine;

namespace WorldGame.SceneManagement
{
    /// <summary>
    /// Composant de démo : joue l'effet de transition sur place (PlayIn → pause → PlayOut)
    /// sans charger de scène. Permet de visualiser l'effet dans la scène Demo.
    /// </summary>
    public class WipeDemoPlayer : MonoBehaviour
    {
        [SerializeField] private GameObject _effectBehaviour; // doit implémenter ITransitionEffect
        [SerializeField, Range(0f, 2f)] private float _holdDuration = 0.4f;

        private bool _isPlaying;

        /// <summary>À brancher sur le OnClick d'un bouton.</summary>
        public void PlayDemo()
        {
            if (_isPlaying)
            {
                return;
            }

            PlayDemoAsync();
        }

        private async void PlayDemoAsync()
        {
            var effect = _effectBehaviour.GetComponents<MonoBehaviour>().ToList().Where(x => x is ITransitionEffect).FirstOrDefault() as ITransitionEffect;
            if (effect == null)
            {
                Debug.LogError("[WipeDemoPlayer] _effectBehaviour n'implémente pas ITransitionEffect.");
                return;
            }

            _isPlaying = true;

            try
            {
                var ct = destroyCancellationToken;

                await effect.PlayInAsync(ct);                          // couvre l'écran
                await Awaitable.WaitForSecondsAsync(_holdDuration, ct); // petite pause visible
                await effect.PlayOutAsync(ct);                         // dévoile à nouveau
            }
            catch (System.OperationCanceledException)
            {
                // annulation normale
            }
            finally
            {
                _isPlaying = false;
            }
        }
    }
}
