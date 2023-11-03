/// PATCH

/// PREPEND
#if TEST2
prepended = true
#elsif TEST
show_debug_message(#TestEnum.Test2)
#endif
/// END

/// REPLACE
#if TEST
quit_timer = 0
#endif
#ifndef TEST
axis_value = 0.4
#endif
/// CODE
#if TEST
quit_timer = 1
#endif
#ifndef TEST
axis_value = 0.5
#endif
/// END

/// AFTER
border_alpha = 1
/// CODE
after_command = true
/// END

/// BEFORE
screenshot = -1
/// CODE
before_command = true
/// END

/// APPEND
appended = true
/// END

// testing enum
/// APPEND
enum_value1 = #TestEnum2.Test1 
enum_value2 = #TestEnum.Test3
enum_value3 = #TestEnum.#length
/// END

/// APPEND
method_result = #TestMethod("First", "Second")
/// END

/// APPEND
#TestGMLMethod(@@
show_debug_message(obj_mainchara)    
$$)
/// END

/// APPEND
show_debug_message(#TestNumberMethod(1, 2.3))
/// END