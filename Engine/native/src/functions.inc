std::string valueText(const Value& x) { if(x.range)throw Error("#VALUE!");if(x.boolean)return x.number?"TRUE":"FALSE";if(!x.numeric)return x.text;std::ostringstream s;s<<std::setprecision(12)<<x.number;return s.str(); }
Value extraFunction(const std::string& name,const std::vector<Value>& all) {
 auto check=[&](size_t min,size_t max){if(all.size()<min||all.size()>max)throw Error("#ARGS!");};
 std::vector<double> ns;for(const auto& x:all)if(x.numeric&&!x.boolean)ns.push_back(x.number);
 if(name=="COUNTA"||name=="COUNTBLANK"){double count=0;for(const auto& x:all)if((!x.numeric&&x.text.empty())==(name=="COUNTBLANK"))count++;return Value(count);}
 if(name=="AND"||name=="OR"){check(1,100000);bool result=name=="AND";for(const auto& x:all){if(name=="AND")result=result&&truth(x);else result=result||truth(x);}return Value::logical(result);}
 if(name=="NOT"){check(1,1);return Value::logical(!truth(all[0]));}
 if(name=="PRODUCT"){double product=ns.empty()?0:1;for(auto n:ns)product*=n;return Value(product);}
 if(name=="MEDIAN"){if(ns.empty())throw Error("#NUM!");std::sort(ns.begin(),ns.end());size_t m=ns.size()/2;return Value(ns.size()%2?ns[m]:(ns[m-1]+ns[m])/2);}
 if(name=="CONCAT"||name=="CONCATENATE"){std::string text;for(const auto& x:all)text+=valueText(x);return Value(text);}
 if(name=="LEN"){check(1,1);auto s=valueText(all[0]);size_t n=0;for(unsigned char c:s)if((c&0xc0)!=0x80)n++;return Value(double(n));}
 if(name=="LOWER"||name=="UPPER"||name=="TRIM"){check(1,1);auto s=valueText(all[0]);if(name=="TRIM"){std::string out;bool space=false;for(char c:s){if(c==' '){if(!out.empty())space=true;}else{if(space)out+=' ';out+=c;space=false;}}return Value(out);}for(char& c:s)if(static_cast<unsigned char>(c)<128)c=char(name=="UPPER"?std::toupper(c):std::tolower(c));return Value(s);}
 if(name=="LEFT"||name=="RIGHT"||name=="MID"){check(name=="MID"?3:1,name=="MID"?3:2);auto s=valueText(all[0]);int start=name=="MID"?int(num(all[1]))-1:0,count=int(all.size()>1?num(all[name=="MID"?2:1]):1);if(start<0||count<0)throw Error("#VALUE!");std::vector<size_t> pos;for(size_t i=0;i<s.size();i++)if((static_cast<unsigned char>(s[i])&0xc0)!=0x80)pos.push_back(i);pos.push_back(s.size());if(name=="RIGHT")start=std::max(0,int(pos.size())-1-count);size_t a=std::min(size_t(start),pos.size()-1),b=std::min(a+size_t(count),pos.size()-1);return Value(s.substr(pos[a],pos[b]-pos[a]));}
 if(name=="SQRT"){check(1,1);double x=num(all[0]);if(x<0)throw Error("#NUM!");return Value(std::sqrt(x));}
 if(name=="INT"){check(1,1);return Value(std::floor(num(all[0])));}
 if(name=="POWER"){check(2,2);return Value(std::pow(num(all[0]),num(all[1])));}
 if(name=="MOD"){check(2,2);double x=num(all[0]),y=num(all[1]);if(!y)throw Error("#DIV/0!");return Value(x-y*std::floor(x/y));}
 if(name=="CEILING"||name=="FLOOR"){check(1,2);double x=num(all[0]),s=std::abs(all.size()==2?num(all[1]):1);return Value(!s?0:(name=="CEILING"?std::ceil(x/s):std::floor(x/s))*s);}
 if(name=="ROUNDUP"||name=="ROUNDDOWN"){check(1,2);double x=num(all[0]),p=std::pow(10,all.size()==2?num(all[1]):0);return Value((x<0?-1:1)*(name=="ROUNDUP"?std::ceil(std::abs(x)*p):std::floor(std::abs(x)*p))/p);}
 throw Error("#NAME?");
}
bool matchesCriterion(const Value& value,const std::string& criterion) {
 std::string op="=",expected=criterion;
 for(const auto& prefix:std::vector<std::string>{"<=",">=","<>","=","<",">"})if(criterion.rfind(prefix,0)==0){op=prefix;expected=criterion.substr(prefix.size());break;}
 auto a=upper(valueText(value)),b=upper(expected);
 if(op=="=")return a==b;if(op=="<>")return a!=b;
 if(!value.numeric||value.boolean)return false;char* end=nullptr;double target=std::strtod(expected.c_str(),&end);if(end==expected.c_str()||*end)return false;
 return op=="<"?value.number<target:op==">"?value.number>target:op=="<="?value.number<=target:value.number>=target;
}
