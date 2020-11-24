namespace MHLab.Patch.Core.Admin.Progresses
{
    public sealed class BuilderProgress
    {
        public int TotalSteps { get; set; }
        public int CurrentSteps { get; set; }
        public string StepMessage { get; set; }
    }
}
