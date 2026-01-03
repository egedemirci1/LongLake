// IInteractable.cs
public interface IInteractable
{
    // NGO mimarisine uygun olarak etkileþime giren oyuncunun envanterini parametre alýyoruz
    void Interact(InventoryManager interactorInventory);

    string GetInteractText();
}