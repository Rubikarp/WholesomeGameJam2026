using System.Collections.Generic;
using System.Threading.Tasks;

namespace LightSide
{
    /// <summary>
    /// Opt-in extension for clipboard providers that cannot (or should not) read on the
    /// caller's thread — WebGL where <c>navigator.clipboard</c> returns Promises, Linux
    /// where reads spawn a subprocess. Callers probe via <c>as IAsyncClipboardProvider</c>;
    /// providers without it fall through to the sync path wrapped in
    /// <see cref="Task.FromResult{TResult}(TResult)"/>.
    /// </summary>
    public interface IAsyncClipboardProvider : IClipboardProvider
    {
        /// <summary>
        /// Async read for <paramref name="format"/>. On WebGL requires a user-activation
        /// context (button click / keyboard handler); resolves to <see langword="null"/>
        /// on permission denial or unsupported format.
        /// </summary>
        Task<string> GetTextAsync(ClipboardFormat format);

        /// <summary>Async availability probe. WebGL returns the sync cached answer; true async check would require user activation.</summary>
        Task<bool> HasFormatAsync(ClipboardFormat format);

        /// <summary>
        /// Reads every present format of <paramref name="formats"/> in ONE underlying
        /// clipboard access. This is the paste pipeline's async entry: on WebGL a single
        /// <c>navigator.clipboard.read()</c> already carries all types, and per-format
        /// reads there burn the transient user activation after the first (Safari /
        /// Firefox reject reads 2..n and Safari shows a paste prompt per read). Resolves
        /// to the items found — possibly empty, never <see langword="null"/>.
        /// </summary>
        Task<IReadOnlyList<ClipboardItem>> GetItemsAsync(IReadOnlyList<ClipboardFormat> formats);
    }
}
