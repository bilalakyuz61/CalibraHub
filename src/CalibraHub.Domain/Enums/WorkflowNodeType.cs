namespace CalibraHub.Domain.Enums;

public enum WorkflowNodeType
{
    Start        = 0,
    Task         = 1,
    Decision     = 2,
    ParallelSplit = 3,
    ParallelJoin = 4,
    End          = 5,
}
