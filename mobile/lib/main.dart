import 'package:flutter/material.dart';

import 'app/dependencies.dart';
import 'app/waiter_app.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final dependencies = await AppDependencies.create();
  runApp(WaiterApp(dependencies: dependencies));
}
