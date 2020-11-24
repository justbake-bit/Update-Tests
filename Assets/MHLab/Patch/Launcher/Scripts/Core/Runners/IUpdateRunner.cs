using System;

namespace MHLab.Patch.Core.Client.Runners
{
    public interface IUpdateRunner
    {
        event EventHandler<IUpdater> PerformedStep;

        void Update();

        void RegisterStep<T>(T updater) where T : IUpdater;

        int GetProgressAmount();
    }
}
