namespace UniversalMediaSorter.Services
{
    /// <summary>
    /// Interface for reverse geocoding services (latitude, longitude -> human-readable location).
    /// </summary>
    public interface IReverseGeocoder
    {
        /// <summary>
        /// Reverse geocode coordinates to a human-readable place name.
        /// </summary>
        /// <returns>Place name, or null if geocoding fails.</returns>
        Task<string?> ReverseGeocodeAsync(double latitude, double longitude);
    }
}
