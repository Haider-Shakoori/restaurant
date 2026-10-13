import 'package:businessos_restaurant_waiter/app/app_theme.dart';
import 'package:businessos_restaurant_waiter/core/format/restaurant_formats.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('formatMoney mirrors desktop AFN {value:N2}', () {
    test('groups thousands, keeps two decimals and the AFN prefix', () {
      expect(formatMoney(1234.5), 'AFN 1,234.50');
      expect(formatMoney('1234.50'), 'AFN 1,234.50');
      expect(formatMoney(1000000), 'AFN 1,000,000.00');
      expect(formatMoney(0), 'AFN 0.00');
      expect(formatMoney('9.9'), 'AFN 9.90');
    });

    test('supports the compact N0 form used by menu tiles', () {
      expect(formatMoney(1200, decimals: 0), 'AFN 1,200');
      expect(formatMoney('120.00', decimals: 0), 'AFN 120');
    });

    test('treats missing or unparseable amounts as zero', () {
      expect(formatMoney(null), 'AFN 0.00');
      expect(formatMoney(''), 'AFN 0.00');
      expect(formatMoney('n/a'), 'AFN 0.00');
    });
  });

  group('formatOrderNumber mirrors the desktop short #n', () {
    test('renders #n without zero padding', () {
      expect(formatOrderNumber(7), '#7');
      expect(formatOrderNumber('42'), '#42');
      expect(formatOrderNumber(0), '');
      expect(formatOrderNumber(null), '');
    });

    test('falls back to the supplied reference when unassigned', () {
      expect(formatOrderNumber(null, fallback: 'abc12345'), 'abc12345');
      expect(formatOrderNumber(0, fallback: 'takeaway-1'), 'takeaway-1');
    });
  });

  group('formatStatus mirrors the desktop uppercase tokens', () {
    test('upper-cases and separates underscore tokens', () {
      expect(formatStatus('ready'), 'READY');
      expect(formatStatus('submitted_pending_sync'), 'SUBMITTED PENDING SYNC');
      expect(formatStatus('dine_in'), 'DINE IN');
      expect(formatStatus(null), '');
    });
  });

  group('formatTableStatus mirrors the desktop title-case floor states', () {
    test('title-cases table states', () {
      expect(formatTableStatus('occupied'), 'Occupied');
      expect(formatTableStatus('available'), 'Available');
      expect(formatTableStatus('out_for_cleaning'), 'Out For Cleaning');
      expect(formatTableStatus(null), '');
    });
  });

  group('date and time mirrors desktop dd MMM yyyy / HH:mm', () {
    test('formats calendar dates and clock times', () {
      final value = DateTime(2026, 10, 13, 9, 5);
      expect(formatDate(value), '13 Oct 2026');
      expect(formatTime(value), '09:05');
      expect(formatDateTime(value), '13 Oct 2026 09:05');
    });

    test('handles single-digit days and months', () {
      expect(formatDate(DateTime(2026, 1, 3)), '03 Jan 2026');
    });
  });

  group('restaurant theme mirrors the desktop brand tokens', () {
    test('uses the desktop orange brand and light surfaces', () {
      final theme = buildRestaurantTheme();
      expect(RestaurantColors.brand, const Color(0xFFF97316));
      expect(theme.colorScheme.primary, const Color(0xFFF97316));
      expect(theme.colorScheme.onPrimary, RestaurantColors.onBrand);
      expect(theme.scaffoldBackgroundColor, RestaurantColors.appBackground);
      expect(theme.appBarTheme.backgroundColor, RestaurantColors.surface);
    });
  });
}
