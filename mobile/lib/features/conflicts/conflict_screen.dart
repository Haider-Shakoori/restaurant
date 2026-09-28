import 'package:flutter/material.dart';

import '../../app/app_strings.dart';
import '../../data/local/local_database.dart';

class ConflictScreen extends StatelessWidget {
  const ConflictScreen({
    required this.database,
    required this.strings,
    super.key,
  });

  final LocalDatabase database;
  final AppStrings strings;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(strings.conflicts)),
      body: FutureBuilder<List<Map<String, Object?>>>(
        future: database.conflicts(),
        builder: (context, snapshot) {
          final conflicts = snapshot.data;

          if (conflicts == null) {
            return const Center(child: CircularProgressIndicator());
          }

          if (conflicts.isEmpty) {
            return const Center(child: Text('No sync conflicts.'));
          }

          return ListView.separated(
            padding: const EdgeInsets.all(16),
            itemCount: conflicts.length,
            separatorBuilder: (_, _) => const Divider(),
            itemBuilder: (context, index) {
              final conflict = conflicts[index];

              return ListTile(
                leading: const Icon(Icons.warning_amber),
                title: Text(conflict['code']!.toString()),
                subtitle: Text(conflict['message']!.toString()),
                trailing: Text(conflict['operation']!.toString()),
              );
            },
          );
        },
      ),
    );
  }
}
