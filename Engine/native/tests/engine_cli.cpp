#include "gaius.h"
#include <iostream>
#include <string>
#include <vector>
int main(){std::string expression,cells;while(std::getline(std::cin,expression)&&std::getline(std::cin,cells)){size_t size=gaius_calculate(expression.c_str(),cells.c_str(),nullptr,0);std::vector<char> output(size);gaius_calculate(expression.c_str(),cells.c_str(),output.data(),size);std::cout<<output.data()<<"\n";}return 0;}
