using Grand.Core;
using Grand.Core.Caching;
using Grand.Core.Data;
using Grand.Core.Domain.Catalog;
using Grand.Core.Domain.Directory;
using Grand.Services.Events;
using Grand.Services.Localization;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Grand.Services.Directory
{
    /// <summary>
    /// Country service
    /// </summary>
    public partial class CountryService : ICountryService
    {
        #region Constants

        /// <summary>
        /// Key for caching
        /// </summary>
        /// <remarks>
        /// {0} : language ID
        /// {1} : show hidden records?
        /// </remarks>
        private const string COUNTRIES_ALL_KEY = "Grand.country.all-{0}-{1}";
        /// <summary>
        /// Key pattern to clear cache
        /// </summary>
        private const string COUNTRIES_PATTERN_KEY = "Grand.country.";

        #endregion
        
        #region Fields

        private readonly IRepository<Country> _countryRepository;
        private readonly IStoreContext _storeContext;
        private readonly CatalogSettings _catalogSettings;
        private readonly IEventPublisher _eventPublisher;
        private readonly ICacheManager _cacheManager;

        #endregion

        #region Ctor

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="cacheManager">Cache manager</param>
        /// <param name="countryRepository">Country repository</param>
        /// <param name="storeMappingRepository">Store mapping repository</param>
        /// <param name="storeContext">Store context</param>
        /// <param name="catalogSettings">Catalog settings</param>
        /// <param name="eventPublisher">Event published</param>
        public CountryService(ICacheManager cacheManager,
            IRepository<Country> countryRepository,
            IStoreContext storeContext,
            CatalogSettings catalogSettings,
            IEventPublisher eventPublisher)
        {
            this._cacheManager = cacheManager;
            this._countryRepository = countryRepository;
            this._storeContext = storeContext;
            this._catalogSettings = catalogSettings;
            this._eventPublisher = eventPublisher;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Deletes a country
        /// </summary>
        /// <param name="country">Country</param>
        public virtual async Task DeleteCountry(Country country)
        {
            if (country == null)
                throw new ArgumentNullException("country");

            await _countryRepository.DeleteAsync(country);

            _cacheManager.RemoveByPattern(COUNTRIES_PATTERN_KEY);

            //event notification
            _eventPublisher.EntityDeleted(country);
        }

        /// <summary>
        /// Gets all countries
        /// </summary>
        /// <param name="languageId">Language identifier. It's used to sort countries by localized names (if specified); pass 0 to skip it</param>
        /// <param name="showHidden">A value indicating whether to show hidden records</param>
        /// <returns>Countries</returns>
        public virtual async Task<IList<Country>> GetAllCountries(string languageId = "", bool showHidden = false)
        {
            string key = string.Format(COUNTRIES_ALL_KEY, languageId, showHidden);

            return await _cacheManager.Get(key, async () =>
            {
                var builder = Builders<Country>.Filter;
                var filter = builder.Empty;

                if (!showHidden)
                    filter = filter & builder.Where(c => c.Published);

                if (!showHidden && !_catalogSettings.IgnoreStoreLimitations)
                {
                    //Store mapping
                    var currentStoreId = new List<string> { _storeContext.CurrentStore.Id };
                    filter = filter & (builder.AnyIn(x => x.Stores, currentStoreId) | builder.Where(x => !x.LimitedToStores));
                }
                var countries = await _countryRepository.Collection.Find(filter).SortBy(x => x.DisplayOrder).ThenBy(x=>x.Name).ToListAsync();
                if (!string.IsNullOrEmpty(languageId))
                {
                    //we should sort countries by localized names when they have the same display order
                    countries = countries
                        .OrderBy(c => c.DisplayOrder)
                        .ThenBy(c => c.GetLocalized(x => x.Name, languageId))
                        .ToList();
                }
                return countries;
            });
        }

        /// <summary>
        /// Gets all countries that allow billing
        /// </summary>
        /// <param name="languageId">Language identifier. It's used to sort countries by localized names (if specified); pass 0 to skip it</param>
        /// <param name="showHidden">A value indicating whether to show hidden records</param>
        /// <returns>Countries</returns>
        public virtual async Task<IList<Country>> GetAllCountriesForBilling(string languageId = "", bool showHidden = false)
        {
            var countries = await GetAllCountries(languageId, showHidden);
            return countries.Where(c => c.AllowsBilling).ToList();
        }

        /// <summary>
        /// Gets all countries that allow shipping
        /// </summary>
        /// <param name="languageId">Language identifier. It's used to sort countries by localized names (if specified); pass 0 to skip it</param>
        /// <param name="showHidden">A value indicating whether to show hidden records</param>
        /// <returns>Countries</returns>
        public virtual async Task<IList<Country>> GetAllCountriesForShipping(string languageId = "", bool showHidden = false)
        {
            var countries = await GetAllCountries(languageId, showHidden);
            return countries.Where(c => c.AllowsShipping).ToList();
        }

        /// <summary>
        /// Gets a country 
        /// </summary>
        /// <param name="countryId">Country identifier</param>
        /// <returns>Country</returns>
        public virtual Task<Country> GetCountryById(string countryId)
        {
            return _countryRepository.GetByIdAsync(countryId);
        }

        /// <summary>
        /// Get countries by identifiers
        /// </summary>
        /// <param name="countryIds">Country identifiers</param>
        /// <returns>Countries</returns>
        public virtual async Task<IList<Country>> GetCountriesByIds(string[] countryIds)
        {
            if (countryIds == null || countryIds.Length == 0)
                return new List<Country>();

            var query = from c in _countryRepository.Table
                        where countryIds.Contains(c.Id)
                        select c;
            var countries = await query.ToListAsync();
            //sort by passed identifiers
            var sortedCountries = new List<Country>();
            foreach (string id in countryIds)
            {
                var country = countries.Find(x => x.Id == id);
                if (country != null)
                    sortedCountries.Add(country);
            }
            return sortedCountries;
        }

        /// <summary>
        /// Gets a country by two letter ISO code
        /// </summary>
        /// <param name="twoLetterIsoCode">Country two letter ISO code</param>
        /// <returns>Country</returns>
        public virtual Task<Country> GetCountryByTwoLetterIsoCode(string twoLetterIsoCode)
        {
            var filter = Builders<Country>.Filter.Eq(x => x.TwoLetterIsoCode, twoLetterIsoCode);
            return _countryRepository.Collection.Find(filter).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Gets a country by three letter ISO code
        /// </summary>
        /// <param name="threeLetterIsoCode">Country three letter ISO code</param>
        /// <returns>Country</returns>
        public virtual Task<Country> GetCountryByThreeLetterIsoCode(string threeLetterIsoCode)
        {
            var filter = Builders<Country>.Filter.Eq(x => x.ThreeLetterIsoCode, threeLetterIsoCode);
            return _countryRepository.Collection.Find(filter).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Inserts a country
        /// </summary>
        /// <param name="country">Country</param>
        public virtual async Task InsertCountry(Country country)
        {
            if (country == null)
                throw new ArgumentNullException("country");

            await _countryRepository.InsertAsync(country);

            _cacheManager.RemoveByPattern(COUNTRIES_PATTERN_KEY);

            //event notification
            _eventPublisher.EntityInserted(country);
        }

        /// <summary>
        /// Updates the country
        /// </summary>
        /// <param name="country">Country</param>
        public virtual async Task UpdateCountry(Country country)
        {
            if (country == null)
                throw new ArgumentNullException("country");

            await _countryRepository.UpdateAsync(country);

            _cacheManager.RemoveByPattern(COUNTRIES_PATTERN_KEY);

            //event notification
            _eventPublisher.EntityUpdated(country);
        }

        #endregion
    }
}