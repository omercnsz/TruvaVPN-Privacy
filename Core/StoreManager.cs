using System;
using System.Threading.Tasks;
using Windows.Services.Store;
using WinRT.Interop;

namespace TruvaDesktop.Core
{
    public class StoreManager
    {
        private static StoreManager? _instance;
        public static StoreManager Instance => _instance ??= new StoreManager();

        private StoreContext? _context;
        private const string SubscriptionId = "premium_monthly_sub";

        private StoreManager() { }

        private StoreContext GetContext(IntPtr windowHandle)
        {
            if (_context == null)
            {
                _context = StoreContext.GetDefault();
                InitializeWithWindow.Initialize(_context, windowHandle);
            }
            return _context;
        }

        public async Task<bool> IsUserSubscribedAsync()
        {
            try
            {
                StoreContext context = StoreContext.GetDefault();
                StoreAppLicense appLicense = await context.GetAppLicenseAsync();

                foreach (var addOnLicense in appLicense.AddOnLicenses)
                {
                    if (addOnLicense.Key == SubscriptionId && addOnLicense.Value.IsActive)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[STORE] Abonelik kontrolü hatası: {ex.Message}");
            }
            return false;
        }

        public async Task<StorePurchaseStatus?> PurchaseSubscriptionAsync(IntPtr windowHandle)
        {
            try
            {
                StoreContext context = GetContext(windowHandle);
                StorePurchaseResult result = await context.RequestPurchaseAsync(SubscriptionId);

                return result.Status;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[STORE] Satın alma hatası: {ex.Message}");
                throw;
            }
        }
    }
}
