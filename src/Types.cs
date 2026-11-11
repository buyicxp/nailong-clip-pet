namespace NailongClipPet
{
    public enum PetState
    {
        Idle,
        CopyHold,
        CutHold,
        PasteCopy,
        PasteCut,
        Cancel
    }

    public enum ClipKind
    {
        None,
        Files,
        Image,
        Text
    }
}
