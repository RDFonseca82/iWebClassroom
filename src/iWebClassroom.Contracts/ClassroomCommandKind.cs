namespace IWebClassroom.Contracts;

public enum ClassroomCommandKind
{
    ShowMessage = 0,
    LaunchApplication = 1,
    StartScreenShare = 2,
    StopScreenShare = 3,
    CheckForUpdate = 4,
    StartBlackout = 5,
    StopBlackout = 6,
    StartStudentView = 7,
    StopStudentView = 8,
    ShowMedia = 9,
    StopMedia = 10
}
