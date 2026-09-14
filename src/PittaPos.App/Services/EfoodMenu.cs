using PittaPos.Core.Efood;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>Ο ζωντανός κατάλογος (MenuStore) όπως τον χρειάζονται ο κατάλογος και οι παραγγελίες του e-food — οι
/// κανόνες έρχονται από την ίδια πηγή με τον customizer, ώστε το e-food να μη διαφέρει ποτέ από το ταμείο.</summary>
public sealed class EfoodMenu : IEfoodMenu
{
    public static EfoodMenu Live { get; } = new();

    private static MenuStore Store => MenuStore.Instance;

    public IReadOnlyList<MenuCategory> Categories => Store.Categories;
    public IReadOnlyList<ExtraItem> Extras => Store.Extras;
    public bool OpensIngredients(Product product) => MenuStore.OpensIngredients(product);
    public IReadOnlyList<string> IngredientsFor(Product product) => Store.IngredientsFor(product);
    public bool HasBreadChoice(string categoryName) => Store.HasBreadChoice(categoryName);
    public bool FuseBreadIntoName(string categoryName) => Store.FuseBreadIntoName(categoryName);
    public bool SupportsDoublePita(string categoryName) => Store.SupportsDoublePita(categoryName);
    public decimal DoublePitaPriceFor(string categoryName) => Store.DoublePitaPriceFor(categoryName);
}
