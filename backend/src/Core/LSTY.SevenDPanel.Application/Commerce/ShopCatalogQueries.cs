using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Commerce
{
    public sealed class ShopProductCursor
    {
        public ShopProductCursor(int sortOrder, string productId)
        {
            SortOrder = sortOrder;
            ProductId = CommerceValidation.RequireText(productId, nameof(productId));
        }

        public int SortOrder { get; }
        public string ProductId { get; }
    }

    public sealed class ShopProductKeysetQuery
    {
        public const int MaximumPageSize = 50;

        public ShopProductKeysetQuery(int pageSize, ShopProductCursor? after = null)
        {
            if (pageSize < 1 || pageSize > MaximumPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            PageSize = pageSize;
            After = after;
        }

        public int PageSize { get; }
        public ShopProductCursor? After { get; }
    }

    public sealed class ShopProductPage
    {
        public ShopProductPage(
            IEnumerable<ShopProductSnapshot> products,
            ShopProductCursor? next)
        {
            Products = (products ?? throw new ArgumentNullException(nameof(products))).ToArray();
            if (Products.Any(product => !product.Enabled))
                throw new ArgumentException("Shop pages can contain only enabled products.", nameof(products));
            Next = next;
        }

        public IReadOnlyList<ShopProductSnapshot> Products { get; }
        public ShopProductCursor? Next { get; }
    }

    public interface IShopCatalogQueryStore
    {
        ShopProductPage QueryEnabledProducts(ShopProductKeysetQuery query);
    }

    public sealed class BrowseShopUseCase
    {
        private readonly IShopCatalogQueryStore store;

        public BrowseShopUseCase(IShopCatalogQueryStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public ShopProductPage Execute(ShopProductKeysetQuery query) =>
            store.QueryEnabledProducts(query ?? throw new ArgumentNullException(nameof(query)));
    }
}
