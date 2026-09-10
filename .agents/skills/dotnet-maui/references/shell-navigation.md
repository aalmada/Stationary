# Shell Navigation

## Route Design

- Use Shell for the application's primary hierarchy: tabs, flyout destinations, and root-level content.
- Explicitly set `Route` for every hierarchy item. Runtime-generated routes are not stable between sessions.
- Register detail pages not declared in the visual hierarchy with `Routing.RegisterRoute` before navigation. Duplicate routes throw `ArgumentException`.
- Use simple, globally unique route names and avoid coupling route strings to view-model type names.

## Navigate Safely

- Await `Shell.Current.GoToAsync` and let calling code handle failures and cancellation.
- Use absolute URIs such as `//home` for root navigation and relative routes for contextual navigation.
- Pass IDs or small immutable values. Resolve domain data at the destination and validate every navigation value, including deep-link values.
- Do not pass secrets or large mutable objects in query strings or navigation parameters.
- Test route resolution, tab switching, modal flows, back navigation, login/logout reset, and deep-link behavior.

## Lifecycle And Interception

- Use `Navigating` to validate/cancel a navigation or coordinate a navigation-specific action. Use `Navigated` for state that requires the completed destination.
- Keep navigation policy in an injectable service or view model when business rules control it; do not embed policy in page event handlers.
- Deregister temporary routes with `Routing.UnRegisterRoute` only when their lifetime is intentionally limited.

## Sources

- [Shell navigation](https://learn.microsoft.com/dotnet/maui/fundamentals/shell/navigation?view=net-maui-10.0)
- [Shell](https://learn.microsoft.com/dotnet/maui/fundamentals/shell/?view=net-maui-10.0)
