Pod::Spec.new do |s|
  s.name = 'gaius_engine'
  s.version = '0.1.0'
  s.summary = 'Gaius native calculation engine'
  s.description = 'Shared C++ calculation engine for Gaius Office.'
  s.homepage = 'https://example.invalid/gaius'
  s.license = { :type => 'Proprietary', :text => 'Project license not yet selected.' }
  s.author = { 'Gaius' => 'development@example.invalid' }
  s.source = { :path => '.' }
  s.source_files = '../src/*.{h,cpp}'
  s.dependency 'Flutter'
  s.platform = :ios, '15.0'
  s.pod_target_xcconfig = { 'DEFINES_MODULE' => 'YES', 'CLANG_CXX_LANGUAGE_STANDARD' => 'c++14', 'HEADER_SEARCH_PATHS' => '"$(PODS_TARGET_SRCROOT)/../../native/include"' }
  s.user_target_xcconfig = { 'OTHER_LDFLAGS' => '$(inherited) -Wl,-u,_gaius_calculate' }
end
