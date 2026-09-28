import 'package:flutter/widgets.dart';

class AppStrings {
  const AppStrings._(this.locale);

  final Locale locale;

  bool get isRtl => locale.languageCode == 'fa' || locale.languageCode == 'ps';

  String get appName => _value(
    en: 'BusinessOS Restaurant',
    fa: 'رستورانت BusinessOS',
    ps: 'BusinessOS رستورانت',
  );

  String get setupTitle => _value(
    en: 'Connect this waiter device',
    fa: 'اتصال دستگاه گارسون',
    ps: 'د ګارسون وسیله ونښلوئ',
  );

  String get restaurantUrl => _value(
    en: 'Restaurant server',
    fa: 'سرور رستورانت',
    ps: 'د رستورانت سرور',
  );

  String get licenseKey => _value(
    en: 'License key',
    fa: 'کلید لایسنس',
    ps: 'د جواز کیلي',
  );

  String get email => _value(en: 'Email', fa: 'ایمیل', ps: 'برېښنالیک');

  String get password => _value(
    en: 'Password',
    fa: 'رمز عبور',
    ps: 'پټنوم',
  );

  String get connect => _value(
    en: 'Activate & sign in',
    fa: 'فعال‌سازی و ورود',
    ps: 'فعاله او ننوتل',
  );

  String get tables => _value(en: 'Tables', fa: 'میزها', ps: 'مېزونه');

  String get sync => _value(en: 'Sync', fa: 'همگام‌سازی', ps: 'همغږي');

  String get offlineQueued => _value(
    en: 'Queued offline',
    fa: 'در صف آفلاین',
    ps: 'آفلاین کتار کې',
  );

  String get conflicts => _value(
    en: 'Conflicts',
    fa: 'تعارض‌ها',
    ps: 'ټکرونه',
  );

  String get guests => _value(en: 'Guests', fa: 'مهمان‌ها', ps: 'مېلمانه');

  String get createOrder => _value(
    en: 'Start order',
    fa: 'شروع سفارش',
    ps: 'امر پیل کړئ',
  );

  String get menu => _value(en: 'Menu', fa: 'منو', ps: 'مینو');

  String get submitOrder => _value(
    en: 'Send to kitchen',
    fa: 'ارسال به آشپزخانه',
    ps: 'پخلنځي ته ولېږئ',
  );

  String get queuedForKitchen => _value(
    en: 'Queued for kitchen sync',
    fa: 'در صف ارسال به آشپزخانه',
    ps: 'پخلنځي همغږۍ ته کتار کې',
  );

  String get add => _value(en: 'Add', fa: 'افزودن', ps: 'زیاتول');

  String get retry => _value(en: 'Retry', fa: 'تلاش دوباره', ps: 'بیا هڅه');

  String _value({
    required String en,
    required String fa,
    required String ps,
  }) {
    return switch (locale.languageCode) {
      'fa' => fa,
      'ps' => ps,
      _ => en,
    };
  }
}
