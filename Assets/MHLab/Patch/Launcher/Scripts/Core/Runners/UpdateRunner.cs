using System;
using System.Collections.Generic;

namespace MHLab.Patch.Core.Client.Runners
{
    public sealed class UpdateRunner : IUpdateRunner
    {
        public event EventHandler<IUpdater> PerformedStep;

        private readonly List<IUpdater> _steps;

        public UpdateRunner()
        {
            _steps = new List<IUpdater>();
        }

        public void Update()
        {
            foreach (var step in _steps)
            {
                step.Update();
                OnPerformedStep(step);
            }
        }

        public void RegisterStep<T>(T step) where T : IUpdater
        {
            _steps.Add(step);
        }

        public int GetProgressAmount()
        {
            var accumulator = 0;

            foreach (var step in _steps)
            {
                accumulator += step.ProgressRangeAmount();
            }

            return accumulator;
        }

        private void OnPerformedStep(IUpdater updater)
        {
            var handler = PerformedStep;
            handler?.Invoke(this, updater);
        }
    }
}