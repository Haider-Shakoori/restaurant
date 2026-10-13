import 'package:flutter/material.dart';

/// Restaurant design tokens mirrored from the desktop POS theme
/// (`desktop/src/BusinessOS.Restaurant.Desktop/Themes/Classic.xaml`) so the
/// Windows and mobile apps render the same brand, surfaces, radii and controls.
class RestaurantColors {
  const RestaurantColors._();

  // Brand / primary actions (desktop BrandPrimaryBrush + button brushes).
  static const brand = Color(0xFFF97316);
  static const brandHover = Color(0xFFEA580C);
  static const brandPressed = Color(0xFFD97706);
  static const brandSoft = Color(0xFFFFF1E6); // BrandPrimarySoftBrush
  static const onBrand = Color(0xFF111827); // ButtonForegroundBrush

  // Surfaces and text (desktop SurfaceBrush / Text*Brush).
  static const appBackground = Color(0xFFF8FAFC);
  static const surface = Color(0xFFFFFFFF);
  static const inputBackground = Color(0xFFFFFFFF);
  static const border = Color(0xFFE2E8F0);
  static const borderStrong = Color(0xFFCBD5E1);
  static const gridHeader = Color(0xFFEAF1F9);
  static const gridAltRow = Color(0xFFF2F6FB);

  static const textPrimary = Color(0xFF0F172A);
  static const textSecondary = Color(0xFF475569);
  static const textMuted = Color(0xFF64748B);

  // Semantic states (desktop Success/Warning/Error brushes).
  static const success = Color(0xFF169B62);
  static const successSoft = Color(0xFFECFDF5);
  static const warning = Color(0xFFB45309);
  static const warningSoft = Color(0xFFFFFBEB);
  static const error = Color(0xFFB91C1C);
  static const errorSoft = Color(0xFFFEF2F2);

  // Disabled actions (desktop ButtonDisabled*Brush).
  static const disabledBackground = Color(0xFFE5E7EB);
  static const disabledForeground = Color(0xFF475569);
}

/// Corner radii shared with the desktop POS (buttons/tabs 12/10, cards 16).
class RestaurantRadii {
  const RestaurantRadii._();

  static const button = 12.0;
  static const input = 12.0;
  static const card = 16.0;
  static const chip = 10.0;
  static const tab = 10.0;
}

/// Builds the app theme from the shared restaurant tokens. Mirrors the desktop
/// Classic theme: orange brand actions, light surfaces, slate text.
ThemeData buildRestaurantTheme() {
  final scheme = ColorScheme.fromSeed(
    seedColor: RestaurantColors.brand,
    brightness: Brightness.light,
  ).copyWith(
    primary: RestaurantColors.brand,
    onPrimary: RestaurantColors.onBrand,
    secondary: RestaurantColors.brandHover,
    onSecondary: RestaurantColors.onBrand,
    secondaryContainer: RestaurantColors.brandSoft,
    onSecondaryContainer: RestaurantColors.brandHover,
    surface: RestaurantColors.surface,
    onSurface: RestaurantColors.textPrimary,
    surfaceContainerHighest: RestaurantColors.gridHeader,
    outline: RestaurantColors.borderStrong,
    error: RestaurantColors.error,
    onError: Colors.white,
  );

  final base = ThemeData(useMaterial3: true, colorScheme: scheme);
  final textTheme = base.textTheme.apply(
    bodyColor: RestaurantColors.textPrimary,
    displayColor: RestaurantColors.textPrimary,
  );

  return base.copyWith(
    scaffoldBackgroundColor: RestaurantColors.appBackground,
    textTheme: textTheme,
    appBarTheme: const AppBarTheme(
      backgroundColor: RestaurantColors.surface,
      foregroundColor: RestaurantColors.textPrimary,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      centerTitle: false,
      titleTextStyle: TextStyle(
        fontSize: 19,
        fontWeight: FontWeight.w800,
        color: RestaurantColors.textPrimary,
      ),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        backgroundColor: RestaurantColors.brand,
        foregroundColor: RestaurantColors.onBrand,
        disabledBackgroundColor: RestaurantColors.disabledBackground,
        disabledForegroundColor: RestaurantColors.disabledForeground,
        minimumSize: const Size(0, 44),
        padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(RestaurantRadii.button),
        ),
        textStyle: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: RestaurantColors.textPrimary,
        minimumSize: const Size(0, 42),
        side: const BorderSide(color: RestaurantColors.borderStrong),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(RestaurantRadii.button),
        ),
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(foregroundColor: RestaurantColors.brandHover),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: RestaurantColors.inputBackground,
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(RestaurantRadii.input),
        borderSide: const BorderSide(color: RestaurantColors.borderStrong),
      ),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(RestaurantRadii.input),
        borderSide: const BorderSide(color: RestaurantColors.borderStrong),
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(RestaurantRadii.input),
        borderSide: const BorderSide(color: RestaurantColors.brand, width: 2),
      ),
    ),
    cardTheme: CardThemeData(
      color: RestaurantColors.surface,
      elevation: 2,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(RestaurantRadii.card),
        side: const BorderSide(color: RestaurantColors.border),
      ),
    ),
    chipTheme: base.chipTheme.copyWith(
      backgroundColor: RestaurantColors.surface,
      surfaceTintColor: Colors.transparent,
      side: const BorderSide(color: RestaurantColors.borderStrong),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(RestaurantRadii.chip),
      ),
    ),
    dividerTheme: const DividerThemeData(
      color: RestaurantColors.border,
      thickness: 1,
      space: 1,
    ),
    segmentedButtonTheme: SegmentedButtonThemeData(
      style: ButtonStyle(
        shape: WidgetStatePropertyAll(
          RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(RestaurantRadii.button),
          ),
        ),
        side: const WidgetStatePropertyAll(
          BorderSide(color: RestaurantColors.borderStrong),
        ),
      ),
    ),
    dialogTheme: DialogThemeData(
      backgroundColor: RestaurantColors.surface,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(RestaurantRadii.card),
      ),
    ),
    bottomSheetTheme: const BottomSheetThemeData(
      backgroundColor: RestaurantColors.surface,
      surfaceTintColor: Colors.transparent,
      dragHandleColor: RestaurantColors.borderStrong,
    ),
    snackBarTheme: const SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      contentTextStyle: TextStyle(color: Colors.white),
    ),
  );
}
