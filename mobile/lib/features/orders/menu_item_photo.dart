import 'package:flutter/material.dart';

import '../../core/models/session_credentials.dart';
import 'menu_image_request.dart';

/// Displays the real catalog photo when one has been uploaded and synced.
/// Never sends device credentials to an external or cloud image host.
class MenuItemPhoto extends StatelessWidget {
  const MenuItemPhoto({
    required this.imageUrl,
    required this.credentials,
    super.key,
  });

  final String? imageUrl;
  final SessionCredentials? credentials;

  @override
  Widget build(BuildContext context) {
    final request = resolveMenuImageRequest(imageUrl, credentials);
    if (request == null) return _placeholder('No photo uploaded');

    return Image.network(
      request.url,
      headers: request.headers,
      fit: BoxFit.cover,
      width: double.infinity,
      loadingBuilder: (context, child, progress) => progress == null
          ? child
          : const Center(child: CircularProgressIndicator(strokeWidth: 2)),
      errorBuilder: (context, error, stackTrace) =>
          _placeholder('Photo unavailable offline'),
    );
  }

  Widget _placeholder(String text) => Container(
        color: const Color(0xFFF0F1F3),
        alignment: Alignment.center,
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Icon(Icons.restaurant_menu_rounded,
                size: 30, color: Color(0xFF85909D)),
            const SizedBox(height: 6),
            Text(text,
                style: const TextStyle(fontSize: 11, color: Color(0xFF697585))),
          ],
        ),
      );
}
