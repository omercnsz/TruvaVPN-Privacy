using System;
using System.Threading.Tasks;
// Microsoft Store desteği için SDK gereklidir. SDK yoksa bu kısımlar pasif kalır.
#if WINDOWS_STORE_SUPPORT
using Windows.Services.Store;
using WinRT.Interop;
#endif

namespace TruvaDesktop.Core
{
    public class StoreManager
    {
        private static StoreManager? _instance;
        public static StoreManager Instance => _instance ??= new StoreManager();

#if WINDOWS_STORE_SUPPORT
        private StoreContext? _context;
#endif
        private const string SubscriptionId = "premium_monthly_sub";

        private StoreManager() { }

#if WINDOWS_STORE_SUPPORT
        private StoreContext GetContext(IntPtr windowHandle)
        {
            if (_context == null)
            {
                _context = StoreContext.GetDefault();
                InitializeWithWindow.Initialize(_context, windowHandle);
            }
            return _context;
        }
#endif

        public async Task<bool> IsUserSubscribedAsync()
        {
#if WINDOWS_STORE_SUPPORT
            try
            {
                StoreContext context = StoreContext.GetDefault();
                StoreAppLicense appLicense = await context.GetAppLicenseAsync();

                foreach (var addOnLicense in appLicense.AddOnLicenses.Values)
                {
                    if (addOnLicense.StoreProductId == SubscriptionId && addOnLicense.IsActive)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[STORE] Abonelik kontrolü hatası: {ex.Message}");
            }
#endif
            return false;
        }

        public async Task<object?> PurchaseSubscriptionAsync(IntPtr windowHandle)
        {
#if WINDOWS_STORE_SUPPORT
            try
            {
                StoreContext context = GetContext(windowHandle);
                StorePurchaseResult result = await context.RequestPurchaseAsync(SubscriptionId);

                return result.Status;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[STORE] Satın alma hatası: {ex.Message}");
            }
#endif
            return null;
        }
    }
}
