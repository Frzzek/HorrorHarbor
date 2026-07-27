namespace HorrorHarbor.Data.Catalog;

public static class CatalogPathResolver
{
    private const string CatalogFileName = "recommendation_catalog.json";

    public static string Resolve() => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "catalog",
        CatalogFileName);
}
