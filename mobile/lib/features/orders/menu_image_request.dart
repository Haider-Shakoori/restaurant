import '../../core/connection/connection_mode.dart';
import '../../core/models/session_credentials.dart';

/// An image request with credentials only for the known local restaurant host.
/// Cloud and arbitrary image hosts never receive device secrets or tokens.
class MenuImageRequest {
  const MenuImageRequest(this.url, this.headers);

  final String url;
  final Map<String, String> headers;
}

MenuImageRequest? resolveMenuImageRequest(
  String? imageUrl,
  SessionCredentials? credentials,
) {
  final value = imageUrl?.trim() ?? '';
  if (value.isEmpty) return null;

  final source = Uri.tryParse(value);
  if (source == null) return null;

  final rootValue =
      credentials?.localBaseUrl ??
      (credentials?.activeChannel == ConnectionChannel.local
          ? credentials?.baseUrl
          : null);
  final root = rootValue == null ? null : Uri.tryParse(rootValue);
  final validRoot = root != null &&
      (root.scheme == 'http' || root.scheme == 'https') &&
      root.host.isNotEmpty;

  final isRelative = !source.hasScheme && !source.hasAuthority;
  Uri resolved;
  if (isRelative) {
    if (!validRoot || !source.path.startsWith('/menu-images/')) return null;
    resolved = root!.resolveUri(source);
  } else {
    resolved = source;
  }

  if ((resolved.scheme != 'http' && resolved.scheme != 'https') ||
      resolved.host.isEmpty) {
    return null;
  }

  final isLocalImage =
      validRoot &&
      credentials != null &&
      resolved.scheme == root?.scheme &&
      resolved.host == root?.host &&
      resolved.port == root?.port &&
      RegExp(r'^/menu-images/[A-Za-z0-9._-]+\.(png|jpe?g|webp)$',
              caseSensitive: false)
          .hasMatch(resolved.path) &&
      !resolved.hasQuery &&
      !resolved.hasFragment;

  if (isRelative && !isLocalImage) return null;

  final headers = isLocalImage && credentials != null
      ? <String, String>{
          'Authorization': 'Bearer ${credentials.accessToken}',
          'X-Device-Id': credentials.deviceId,
          'X-Device-Secret': credentials.deviceSecret,
          'X-App-Version': '1.0.0',
        }
      : const <String, String>{};

  return MenuImageRequest(resolved.toString(), headers);
}
