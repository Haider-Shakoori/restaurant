class ApiException implements Exception {
  const ApiException({
    required this.code,
    required this.message,
    this.statusCode,
    this.payload,
  });

  final String code;
  final String message;
  final int? statusCode;
  final Object? payload;

  @override
  String toString() => 'ApiException($code, $message)';
}
