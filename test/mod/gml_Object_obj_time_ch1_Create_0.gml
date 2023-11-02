/// PATCH

/// PREPEND
prepended = true
/// END

/// REPLACE
quit_timer = 0
/// CODE
quit_timer = 1
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