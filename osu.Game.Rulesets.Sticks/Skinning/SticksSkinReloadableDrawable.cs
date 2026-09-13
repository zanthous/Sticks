#nullable enable

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Framework.Threading;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Sticks.Skinning
{
    /// <summary>
    /// Refreshes optional Sticks skin assets on the update thread. A missing skin source
    /// leaves standalone editor previews and test scenes using their built-in visuals.
    /// </summary>
    public abstract partial class SticksSkinReloadableDrawable : CompositeDrawable
    {
        [Resolved(canBeNull: true)]
        protected ISkinSource? CurrentSkin { get; private set; }

        private ScheduledDelegate? pendingSkinChange;
        private bool disposed;

        public event Action? OnSkinChanged;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (CurrentSkin != null)
                CurrentSkin.SourceChanged += InvalidateSkin;

            refreshSkin();
        }

        protected virtual void SkinChanged(ISkinSource? skin)
        {
        }

        protected void InvalidateSkin()
        {
            if (disposed || !IsLoaded)
                return;

            pendingSkinChange?.Cancel();
            pendingSkinChange = Schedule(refreshSkin);
        }

        private void refreshSkin()
        {
            pendingSkinChange = null;
            if (disposed)
                return;

            SkinChanged(CurrentSkin);
            OnSkinChanged?.Invoke();
        }

        protected override void Dispose(bool isDisposing)
        {
            disposed = true;
            pendingSkinChange?.Cancel();
            pendingSkinChange = null;

            if (CurrentSkin != null)
                CurrentSkin.SourceChanged -= InvalidateSkin;

            OnSkinChanged = null;
            base.Dispose(isDisposing);
        }
    }
}
