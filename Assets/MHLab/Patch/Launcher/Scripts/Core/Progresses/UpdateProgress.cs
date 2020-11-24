using System.Threading;

namespace MHLab.Patch.Core.Client.Progresses
{
    public sealed class UpdateProgress
    {
        public int TotalSteps { get; set; }

        private int _currentSteps;
        public int CurrentSteps
        {
            get => _currentSteps;
            set
            {
                if (value > TotalSteps) _currentSteps = TotalSteps;
                _currentSteps = value;
            }
        }

        public string StepMessage { get; set; }

        public void IncrementStep()
        {
            Interlocked.Increment(ref _currentSteps);
        }
    }
}
