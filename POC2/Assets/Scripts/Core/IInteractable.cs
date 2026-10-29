// 플레이어가 조준해 E로 사용하는 대상.
public interface IInteractable
{
    bool CanInteract();
    string GetPrompt();
    void Interact();
}
